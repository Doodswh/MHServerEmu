using MHServerEmu.Core.Collisions;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Locomotion;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Powers;
using MHServerEmu.Games.Powers.Conditions;
using MHServerEmu.Games.Properties;
using MHServerEmu.Games.Regions;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// How a <see cref="ScriptCombatAI"/> brain behaves. Scripts create one, change what they need and pass it to
    /// <see cref="ScriptCombatAI.Attach"/> (or to <see cref="MirrorImageOptions.AI"/>).
    /// </summary>
    public sealed class CombatAIOptions
    {
        /// <summary>Enemies this close to the leader (or to the agent itself, without a leader) are attacked.</summary>
        public float AggroRange = 1000f;

        /// <summary>With a leader: an idle agent further than this from the leader walks back...</summary>
        public float FollowStartRange = 350f;

        /// <summary>...until it is this close.</summary>
        public float FollowStopRange = 180f;

        /// <summary>With a leader: further than this, the agent jumps straight back to the leader.</summary>
        public float TeleportRange = 2500f;

        /// <summary>Multiplies the agent's movement speed (1.25 = 25% faster than its own run speed).</summary>
        public float MoveSpeedScale = 1f;

        /// <summary>Seconds between any two powers.</summary>
        public float AttackDelaySeconds = 0.4f;

        /// <summary>A channelled power is released after this long (and at once when its target dies).</summary>
        public float MaxChannelSeconds = 2.5f;

        /// <summary>Enemies near the target that count as a pack: area powers and big cooldowns are worth using on one.</summary>
        public int PackSize = 3;

        /// <summary>Enemies within this distance of the target belong to its pack.</summary>
        public float PackRadius = 350f;

        /// <summary>Powers with at least this cooldown are "big": saved for bosses, elites and packs.</summary>
        public float BigCooldownSeconds = 15f;

        /// <summary>Below this share of its health the agent uses a healing power if it has one.</summary>
        public float LowHealthPct = 0.4f;

        /// <summary>Use signature powers (on bosses, elites and packs).</summary>
        public bool UseSignatures = true;

        /// <summary>Use ultimate powers (on bosses and large packs).</summary>
        public bool UseUltimates = true;

        /// <summary>Use movement powers (dashes, leaps) to reach targets that are far away.</summary>
        public bool UseMovementPowers = true;

        /// <summary>Use summoning powers.</summary>
        public bool UseSummons = true;

        /// <summary>Use self buffs and keep them up, and switch toggle powers on.</summary>
        public bool UseBuffs = true;

        /// <summary>The agent's powers cost it nothing (spirit and other resources): bodies usually have no such resources.</summary>
        public bool FreePowers = true;

        /// <summary>Writes every decision (which power, why, its score) to the server log, and why nothing could be used.</summary>
        public bool LogDecisions = false;
    }

    /// <summary>
    /// A combat brain for any agent, driven by its powers' own game data instead of a hand-written script per character.
    /// It replaces the agent's built-in AI. Call from the game thread that owns the agent.
    /// </summary>
    /// <remarks>
    /// What it does every think (4 times a second):
    ///  - Works out the situation: its target (bosses first, then elites, then the nearest), how many enemies stand around
    ///    the target, how far away it is, its own health.
    ///  - Upkeep first: toggles on, heal when low, self buffs that have run out, summons.
    ///  - Then picks the best attack by score: stronger powers (longer cooldown, signature, ultimate) score higher, area
    ///    powers score by how many enemies they would hit, debuffs score high on a tough target that does not have them yet,
    ///    powers that buff the user score high while that buff is down, and any damage bonus the agent currently has for a
    ///    power or its keywords (from a buff, a talent, a trait) raises that power's score: so it uses what was just amped.
    ///  - Big cooldowns, signatures and ultimates are saved for bosses, elites and packs.
    ///  - Out of reach: closes the distance with a movement power if it has one, otherwise walks to the range of its
    ///    longest ready attack (so ranged characters stay at range).
    ///  - With a leader (e.g. a mirror image's owner): stays near them and only fights around them.
    /// </remarks>
    public static class ScriptCombatAI
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private const float ThinkSeconds = 0.25f;
        private const float MeleeChaseRange = 60f;
        private const float DashMinDistance = 450f;       // from this far away, movement attacks are preferred (they close the distance)
        private const float SelfCastRange = 400f;         // self-cast signatures / ultimates are used within this distance of the target
        private const double MaxPowerMS = 6000;           // safety: no single power keeps the agent busy longer than this
        private const double MaxMovementPowerMS = 2000;   // a dash or leap is over by then; end it if the game still has it active
        private const double RetargetMS = 2000;           // how often it looks for a better target than the current one
        private const double FailureLogMS = 3000;         // LogDecisions: how often "nothing usable" is explained
        private const float BuffRecastSeconds = 8f;       // a self buff is recast at most this often
        private const float SummonRecastSeconds = 10f;    // a summon is recast at most this often
        private const double BonusRepeatMS = 5000;        // a power's debuff / self buff bonus counts once per this long

        /// <summary>
        /// Gives <paramref name="agent"/> a combat brain. <paramref name="leader"/> (optional) is who it follows and fights
        /// around. <paramref name="powers"/> (optional) limits it to those powers; by default it uses every activated power
        /// the agent has. The agent's own AI is switched off. <paramref name="onEnded"/> (optional) is called once when the
        /// brain stops because the agent is gone. Returns <see langword="false"/> on failure.
        /// </summary>
        public static bool Attach(Agent agent, WorldEntity leader = null, CombatAIOptions options = null, IEnumerable<PrototypeId> powers = null,
            Action onEnded = null)
        {
            if (agent == null || agent.IsInWorld == false)
                return false;

            agent.AIController?.SetIsEnabled(false);

            options ??= new();
            if (options.FreePowers)
                agent.Properties[PropertyEnum.NoEnduranceCosts] = true;

            Brain brain = new(agent.Game, agent.Id, leader?.Id ?? Entity.InvalidId, options ?? new());
            brain.LearnPowers(agent, powers);

            brain.OnEnded = onEnded;
            brain.ScheduleThink();
            return true;
        }

        private enum Kind
        {
            Attack,     // damages enemies
            Buff,       // helps the user (no enemy target)
            Heal,       // heals the user
            Summon,     // brings an ally
            Toggle,     // stays on once activated
        }

        // What the brain knows about one power, worked out once from its prototype
        private sealed class PowerInfo
        {
            public PrototypeId Ref;
            public PowerPrototype Proto;
            public Kind Kind;
            public bool IsArea;             // hits several enemies
            public bool IsSelfCentered;     // the area is around the user
            public bool AppliesDebuff;      // puts a debuff condition on its target
            public bool AppliesSelfBuff;    // puts a buff condition on its user as well
            public bool IsMovement;         // a movement power (dash, leap, teleport strike): also closes distance
            public bool IsSelfCast;         // used on the agent itself, not aimed at an enemy
            public float MinRecastSeconds;  // never used again sooner than this
            public TimeSpan BonusTime;      // until then its "debuff the target" / "gain its buff" bonus does not apply again
            public float Strength;          // rough power level: 1 for a filler, more for cooldowns, signatures, ultimates
            public TimeSpan ReadyTime;      // our own cooldown clock (the power's cooldown is checked too)
        }

        // One attack the brain is considering this think
        private readonly struct Candidate
        {
            public static readonly Comparison<Candidate> ByScoreDescending = static (a, b) => b.Score.CompareTo(a.Score);

            public readonly PowerInfo Info;
            public readonly Power Power;
            public readonly float Score;
            public readonly string Reason;

            public Candidate(PowerInfo info, Power power, float score, string reason)
            {
                Info = info;
                Power = power;
                Score = score;
                Reason = reason;
            }
        }

        private sealed class Brain
        {
            private readonly Game _game;
            private readonly ulong _agentId;
            private readonly ulong _leaderId;
            private readonly CombatAIOptions _options;

            private readonly List<PowerInfo> _powers = new();
            private TimeSpan _nextPowerTime;
            private TimeSpan _powerStartTime;
            private TimeSpan _retargetTime;
            private TimeSpan _failureLogTime;
            private PrototypeId _lastPower;
            private ulong _targetId;

            // Reused every think, so a brain allocates nothing while it runs
            private readonly List<Candidate> _candidates = new();
            private readonly Action _think;

            public Action OnEnded;

            public Brain(Game game, ulong agentId, ulong leaderId, CombatAIOptions options)
            {
                _game = game;
                _agentId = agentId;
                _leaderId = leaderId;
                _options = options;
                _think = Think;
            }

            public void ScheduleThink() => ScriptTimer.After(_game, ThinkSeconds, _think);

            private void End()
            {
                Action onEnded = OnEnded;
                OnEnded = null;
                onEnded?.Invoke();
            }

            #region Learning the powers

            public void LearnPowers(Agent agent, IEnumerable<PrototypeId> powers)
            {
                if (powers != null)
                {
                    foreach (PrototypeId powerRef in powers)
                        Learn(agent.GetPower(powerRef));
                }
                else if (agent.PowerCollection != null)
                {
                    foreach (var kvp in agent.PowerCollection)
                        Learn(kvp.Value.Power);
                }

                if (_options.LogDecisions)
                {
                    Logger.Info($"[CombatAI] [{agent.PrototypeName}] knows {_powers.Count} powers:" + string.Concat(_powers.Select(info =>
                        $"\n    {info.Proto}: {info.Kind}{(info.IsArea ? ", area" : "")}{(info.IsMovement ? ", movement" : "")}" +
                        $"{(info.IsSelfCast ? ", self-cast" : "")}{(info.AppliesDebuff ? ", debuff" : "")}" +
                        $"{(info.AppliesSelfBuff ? ", self buff" : "")}{(info.Proto.IsSignature ? ", signature" : "")}" +
                        $"{(info.Proto.IsUltimate ? ", ultimate" : "")}, strength {info.Strength:0.#}")));
                }
            }

            private void Learn(Power power)
            {
                PowerPrototype proto = power?.Prototype;
                if (proto == null || _powers.Any(info => info.Ref == proto.DataRef))
                    return;

                // Only powers something has to decide to use: no passives, procs, combo steps, travel powers
                if (proto.PowerCategory != PowerCategoryType.NormalPower || proto.Activation == PowerActivationType.Passive || proto.IsTravelPower)
                    return;

                if ((proto.IsUltimate && _options.UseUltimates == false) || (proto.IsSignature && _options.UseSignatures == false))
                    return;

                TargetingReachPrototype reach = proto.GetTargetingReach();
                TargetingStylePrototype style = proto.GetTargetingStyle();
                if (reach == null || style == null)
                    return;

                PowerInfo info = new() { Ref = proto.DataRef, Proto = proto };

                // What its conditions do, and to whom
                bool targetsEnemies = reach.TargetsEnemy;
                bool hasBuffCondition = false;      // any condition that is not a debuff
                if (proto.AppliesConditions != null)
                {
                    foreach (var item in proto.AppliesConditions)
                    {
                        if (item.Prototype is not ConditionPrototype conditionProto)
                            continue;

                        if (conditionProto.ConditionType != ConditionType.Debuff)
                            hasBuffCondition = true;

                        if (conditionProto.Scope == ConditionScopeType.User && conditionProto.ConditionType != ConditionType.Debuff)
                            info.AppliesSelfBuff = true;
                        else if (conditionProto.Scope == ConditionScopeType.Target && conditionProto.ConditionType == ConditionType.Debuff && targetsEnemies)
                            info.AppliesDebuff = true;
                    }
                }

                // What kind of power it is
                info.IsMovement = proto is MovementPowerPrototype;
                info.IsSelfCast = targetsEnemies == false;

                if (proto.IsToggled)
                    info.Kind = Kind.Toggle;
                else if (IsPetSummon(proto))
                    info.Kind = Kind.Summon;
                else if (targetsEnemies)
                    info.Kind = Kind.Attack;    // includes movement attacks (dash strikes, leaps)
                else if (proto.IsUltimate || proto.IsSignature)
                    info.Kind = Kind.Attack;    // a self-cast signature / ultimate: chosen like an attack, so it is saved for a worthy fight
                else if (info.IsMovement)
                    return;                     // plain repositioning with no enemy target: nothing to decide with
                else if (HasHealing(power))
                    info.Kind = Kind.Heal;
                else if (hasBuffCondition)
                    info.Kind = Kind.Buff;      // puts a buff on the user
                else
                    info.Kind = Kind.Attack;    // self-cast with no buff: an effect around the user (a hotspot, a nova): an attack

                if ((info.IsMovement && _options.UseMovementPowers == false) || (info.Kind == Kind.Summon && _options.UseSummons == false) ||
                    ((info.Kind == Kind.Buff || info.Kind == Kind.Toggle) && _options.UseBuffs == false))
                    return;

                info.IsArea = style.TargetsAOE();
                info.IsSelfCentered = style.AOESelfCentered;

                // A self-cast attack happens around the user
                if (info.Kind == Kind.Attack && info.IsSelfCast)
                {
                    info.IsArea = true;
                    info.IsSelfCentered = true;
                }

                // Upkeep powers are not repeated back to back, even if nothing shows that the last use worked
                info.MinRecastSeconds = info.Kind switch
                {
                    Kind.Buff => BuffRecastSeconds,
                    Kind.Summon => SummonRecastSeconds,
                    _ => 0f,
                };

                // A rough power level from what the designers gave it: cooldown, signature, ultimate
                float cooldownSeconds = (float)Power.GetCooldownDuration(proto, power.Owner, power.Properties).TotalSeconds;
                info.Strength = 1f + cooldownSeconds / 4f;
                if (proto.IsSignature) info.Strength *= 2f;
                if (proto.IsUltimate) info.Strength *= 3f;

                _powers.Add(info);
            }

            // A summon power that brings an agent (a pet), not a hotspot or a prop
            private static bool IsPetSummon(PowerPrototype proto)
            {
                if (proto is not SummonPowerPrototype summonProto || summonProto.SummonEntityContexts == null)
                    return false;

                foreach (SummonEntityContextPrototype context in summonProto.SummonEntityContexts)
                {
                    if (context != null && context.SummonEntity.As<AgentPrototype>() != null)
                        return true;
                }

                return false;
            }

            private static bool HasHealing(Power power)
            {
                PropertyCollection properties = power.Properties;
                return properties.HasProperty(PropertyEnum.HealingBasePct) || properties.HasProperty(PropertyEnum.HealingBase) ||
                    properties.HasProperty(PropertyEnum.HealingBaseCurve) || properties.HasProperty(PropertyEnum.HealingOverTimeBasePct) ||
                    properties.HasProperty(PropertyEnum.HealingOverTimeBase);
            }

            #endregion

            #region Think

            public void Think()
            {
                Agent agent = _game.EntityManager.GetEntity<Agent>(_agentId);
                if (agent == null || agent.IsDestroyed || agent.IsInWorld == false || agent.IsDead)
                {
                    End();
                    return;   // gone: the loop ends here
                }

                WorldEntity leader = null;
                if (_leaderId != Entity.InvalidId)
                {
                    leader = _game.EntityManager.GetEntity<WorldEntity>(_leaderId);
                    if (leader == null || leader.IsInWorld == false || leader.Region != agent.Region)
                    {
                        ScriptSpawner.Despawn(_game, _agentId);   // a follower does not outlive its leader's stay in the region
                        End();
                        return;
                    }
                }

                try
                {
                    Update(agent, leader);
                }
                catch (Exception e)
                {
                    Logger.Warn($"Think(): combat AI of [{agent}] failed: {e.Message}");
                }

                ScheduleThink();
            }

            private void Update(Agent agent, WorldEntity leader)
            {
                TimeSpan now = _game.CurrentTime;

                // Let a power finish before doing anything else. A channelled or held power never ends on its own (a player
                // would release the button): stop it when its target is gone or it has run long enough.
                Power activePower = agent.IsExecutingPower ? agent.ActivePower : null;
                if (activePower != null)
                {
                    double activeMS = (now - _powerStartTime).TotalMilliseconds;
                    bool targetGone = IsValidTarget(agent, _game.EntityManager.GetEntity<WorldEntity>(_targetId)) == false;

                    // A movement power (dash, leap) that is still "active" long after it started would block every other power
                    bool stop;
                    if (activePower.IsPartOfAMovementPower())
                        stop = activeMS >= MaxMovementPowerMS;
                    else if (activePower.IsChanneling)
                        stop = targetGone || activeMS >= _options.MaxChannelSeconds * 1000.0;
                    else
                        stop = activeMS >= MaxPowerMS;

                    if (stop == false)
                        return;

                    activePower.EndPower(EndPowerFlags.ExplicitCancel);
                    if (agent.IsExecutingPower)
                        return;   // still winding down: look again next think
                }

                Vector3 position = agent.RegionLocation.Position;

                if (leader != null)
                {
                    Vector3 leaderPosition = leader.RegionLocation.Position;
                    if (Vector3.Distance2D(position, leaderPosition) > _options.TeleportRange)
                    {
                        agent.Locomotor?.Stop();
                        agent.ChangeRegionPosition(leaderPosition, leader.RegionLocation.Orientation, ChangePositionFlags.Teleport);
                        _targetId = Entity.InvalidId;
                        return;
                    }
                }

                WorldEntity target = leader != null && leader.IsDead ? null : GetTarget(agent, leader, now);
                if (target == null)
                {
                    // Nothing to fight: keep toggles on, stay with the leader
                    if (now >= _nextPowerTime)
                        TryUpkeep(agent, null, now, false);

                    if (leader != null && Vector3.Distance2D(position, leader.RegionLocation.Position) > _options.FollowStartRange)
                        Follow(agent, leader.Id, _options.FollowStopRange);
                    else if (leader == null)
                        agent.Locomotor?.Stop();

                    return;
                }

                agent.OrientToward(target.RegionLocation.Position);

                if (now < _nextPowerTime)
                    return;   // between powers: hold position

                if (TryUpkeep(agent, target, now, true))
                    return;

                if (TryAttack(agent, target, now))
                    return;

                // Nothing usable from here: get closer, to the reach of the longest ready attack
                Follow(agent, target.Id, GetApproachRange(agent, now));
            }

            #endregion

            #region Targets

            // Keeps the current target while it is valid; now and then looks for a better one (a boss, an elite, a nearer enemy)
            private WorldEntity GetTarget(Agent agent, WorldEntity leader, TimeSpan now)
            {
                Vector3 anchor = (leader ?? agent).RegionLocation.Position;

                WorldEntity current = _game.EntityManager.GetEntity<WorldEntity>(_targetId);
                bool currentValid = IsValidTarget(agent, current) && Vector3.Distance2D(current.RegionLocation.Position, anchor) <= _options.AggroRange * 1.25f;

                if (currentValid && now < _retargetTime)
                    return current;

                _retargetTime = now + TimeSpan.FromMilliseconds(RetargetMS);

                Region region = agent.Region;
                if (region == null)
                    return currentValid ? current : null;

                Vector3 position = agent.RegionLocation.Position;
                WorldEntity best = null;
                float bestScore = float.MinValue;

                Sphere volume = new(anchor, _options.AggroRange);
                foreach (WorldEntity entity in region.IterateEntitiesInVolume(volume, new(EntityRegionSPContextFlags.PrimaryPartition)))
                {
                    if (IsValidTarget(agent, entity) == false)
                        continue;

                    // Tougher enemies first, then nearer ones; the current target gets a small bonus so it is not dropped lightly
                    float score = Toughness(entity) * 1000f - Vector3.Distance2D(position, entity.RegionLocation.Position);
                    if (entity == current)
                        score += 200f;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = entity;
                    }
                }

                _targetId = best?.Id ?? Entity.InvalidId;
                return best;
            }

            private static bool IsValidTarget(Agent agent, WorldEntity entity)
            {
                // Enemies only: no barrels, crates and other breakable props
                return entity is Agent && entity.IsDestructible == false
                    && entity.IsInWorld && entity.IsDead == false && entity.IsDestroyed == false
                    && entity.Properties[PropertyEnum.Untargetable] == false
                    && entity.Properties[PropertyEnum.Invulnerable] == false
                    && agent.IsHostileTo(entity);
            }

            // 0 = ordinary enemy, 1 = champion / elite / mini-boss, 2 = boss
            private static int Toughness(WorldEntity entity)
            {
                RankPrototype rank = entity.GetRankPrototype();
                if (rank == null) return 0;
                if (rank.IsRankBoss) return 2;
                return rank.IsRankChampionOrEliteOrMiniBoss ? 1 : 0;
            }

            private int CountEnemiesNear(Agent agent, Vector3 center, float radius)
            {
                Region region = agent.Region;
                if (region == null)
                    return 0;

                int count = 0;
                foreach (WorldEntity entity in region.IterateEntitiesInVolume(new Sphere(center, radius), new(EntityRegionSPContextFlags.PrimaryPartition)))
                {
                    if (IsValidTarget(agent, entity))
                        count++;
                }

                return count;
            }

            #endregion

            #region Choosing powers

            private bool IsReady(Agent agent, PowerInfo info, TimeSpan now, out Power power)
            {
                power = agent.GetPower(info.Ref);
                return power != null && now >= info.ReadyTime && power.IsOnCooldown() == false;
            }

            // Toggles, healing, self buffs, summons. Returns true if a power was used.
            private bool TryUpkeep(Agent agent, WorldEntity target, TimeSpan now, bool inCombat)
            {
                long health = agent.Properties[PropertyEnum.Health];
                long healthMax = agent.Properties[PropertyEnum.HealthMax];
                bool lowHealth = healthMax > 0 && health < healthMax * _options.LowHealthPct;

                foreach (PowerInfo info in _powers)
                {
                    if (info.Kind == Kind.Attack)
                        continue;

                    if (IsReady(agent, info, now, out Power power) == false)
                        continue;

                    string reason;
                    switch (info.Kind)
                    {
                        case Kind.Toggle:
                            if (power.IsToggledOn()) continue;
                            reason = "toggle on";
                            break;

                        case Kind.Heal:
                            if (lowHealth == false) continue;
                            reason = "low health";
                            break;

                        case Kind.Buff:
                            if (inCombat == false || HasConditionFrom(agent, info.Ref, agent.Id)) continue;

                            // A buff on a long cooldown is saved like any other big power
                            if (Power.GetCooldownDuration(info.Proto, agent, power.Properties).TotalSeconds >= _options.BigCooldownSeconds && IsWorthBigPower(agent, target) == false)
                                continue;

                            reason = "buff is down";
                            break;

                        case Kind.Summon:
                            if (inCombat == false) continue;
                            reason = "summon";
                            break;

                        default:
                            continue;
                    }

                    // Powers that are not aimed at an enemy are used on the agent itself; summons go next to the target
                    WorldEntity powerTarget = info.Kind == Kind.Summon && target != null ? target : agent;
                    if (Use(agent, info, power, powerTarget, now, reason, 0f))
                        return true;
                }

                return false;
            }

            // Scores every ready attack against the current situation and uses the best one. Returns true if a power was used.
            private bool TryAttack(Agent agent, WorldEntity target, TimeSpan now)
            {
                Vector3 targetPosition = target.RegionLocation.Position;
                float distance = Vector3.Distance2D(agent.RegionLocation.Position, targetPosition);

                int toughness = Toughness(target);
                int pack = CountEnemiesNear(agent, targetPosition, _options.PackRadius);
                int packAroundSelf = CountEnemiesNear(agent, agent.RegionLocation.Position, _options.PackRadius);
                bool isPack = pack >= _options.PackSize;

                List<Candidate> candidates = _candidates;
                candidates.Clear();
                bool log = _options.LogDecisions;

                foreach (PowerInfo info in _powers)
                {
                    if (info.Kind != Kind.Attack)
                        continue;

                    if (IsReady(agent, info, now, out Power power) == false)
                        continue;

                    // A self-cast signature / ultimate is used once the agent is in the fight, not from across the room
                    if (info.IsSelfCast && distance > SelfCastRange)
                        continue;

                    // An area around the user is no use while the target is outside it
                    if (info.IsArea && info.IsSelfCentered && info.IsSelfCast == false && distance > Math.Max(power.GetApplicationRange(), info.Proto.Radius))
                        continue;

                    // Big cooldowns, signatures and ultimates are saved for something worth it
                    bool isBig = info.Proto.IsSignature || info.Proto.IsUltimate ||
                        Power.GetCooldownDuration(info.Proto, agent, power.Properties).TotalSeconds >= _options.BigCooldownSeconds;

                    if (info.Proto.IsUltimate && toughness < 2 && pack < _options.PackSize * 2)
                        continue;

                    if (isBig && toughness == 0 && isPack == false)
                        continue;

                    float score = info.Strength;
                    string reason = isBig ? "big power" : "attack";

                    // Area powers by how many enemies they would hit
                    if (info.IsArea)
                    {
                        int hits = info.IsSelfCentered ? packAroundSelf : pack;
                        if (info.Proto.MaxAOETargets > 0)
                            hits = Math.Min(hits, info.Proto.MaxAOETargets);

                        score *= Math.Clamp(hits, 1, 8);
                        if (hits >= _options.PackSize)
                            reason = log ? $"area on {hits} enemies" : "area";
                    }
                    else if (toughness > 0)
                    {
                        score *= 1.5f;   // single target powers belong on the tough ones
                    }

                    // Debuff a tough target that does not have it yet
                    // (once per few seconds: some conditions are too brief to see, and must not make the power a favourite)
                    bool bonusFree = now >= info.BonusTime;

                    if (bonusFree && info.AppliesDebuff && toughness > 0 && HasConditionFrom(target, info.Ref, agent.Id) == false)
                    {
                        score *= 3f;
                        reason = "debuff the target";
                    }

                    // Powers that buff the user: while that buff is down
                    if (bonusFree && info.AppliesSelfBuff && HasConditionFrom(agent, info.Ref, agent.Id) == false)
                    {
                        score *= 2.5f;
                        reason = "gain its buff";
                    }

                    // Whatever currently amps this power: bonuses the agent has for this power or its keywords
                    float amp = GetDamageBonus(agent, info.Proto);
                    if (amp > 0f)
                    {
                        score *= 1f + amp;
                        if (log)
                            reason += $", amped +{amp:P0}";
                    }

                    // A movement attack is the way to reach a far target
                    if (info.IsMovement && info.IsSelfCast == false && distance >= DashMinDistance)
                    {
                        score *= 2f;
                        reason = "close the distance";
                    }

                    // Do not repeat the same power while there are others
                    if (info.Ref == _lastPower)
                        score *= 0.5f;

                    candidates.Add(new Candidate(info, power, score, reason));
                }

                // Best first; the first one that can really be used from here wins
                candidates.Sort(Candidate.ByScoreDescending);

                foreach (Candidate candidate in candidates)
                {
                    WorldEntity powerTarget = candidate.Info.IsSelfCast ? agent : target;
                    if (Use(agent, candidate.Info, candidate.Power, powerTarget, now, candidate.Reason, candidate.Score))
                        return true;
                }

                // Nothing could be used: say why, now and then
                if (_options.LogDecisions && now >= _failureLogTime)
                {
                    _failureLogTime = now + TimeSpan.FromMilliseconds(FailureLogMS);
                    // Being stunned, knocked down and the like stops every power: that is the enemy's doing, not a fault
                    string status = string.Join(", ", new[]
                    {
                        agent.IsStunned ? "stunned" : null,
                        agent.IsInKnockdown ? "knocked down" : null,
                        agent.IsInKnockback ? "knocked back" : null,
                        agent.IsInKnockup ? "knocked up" : null,
                        agent.IsImmobilized ? "immobilized" : null,
                        agent.HasAIControlPowerLock ? "AI power lock" : null,
                    }.Where(s => s != null));

                    Logger.Info($"[CombatAI] [{agent.PrototypeName}] nothing usable on [{target.PrototypeName}] at distance {distance:0} " +
                        $"(active power: {(agent.IsExecutingPower ? agent.ActivePowerRef.GetName() : "none")}, status: {(status.Length > 0 ? status : "normal")}):" +
                        string.Concat(candidates.Select(c =>
                            $"\n    {c.Info.Proto}: {agent.CanActivatePower(c.Power, (c.Info.IsSelfCast ? agent : target).Id, (c.Info.IsSelfCast ? agent : target).RegionLocation.Position)}" +
                            $" (range {c.Power.GetRange():0})")));
                }

                return false;
            }

            // A boss, an elite, or a pack: worth a big cooldown
            private bool IsWorthBigPower(Agent agent, WorldEntity target)
            {
                if (target == null)
                    return false;

                return Toughness(target) > 0 || CountEnemiesNear(agent, target.RegionLocation.Position, _options.PackRadius) >= _options.PackSize;
            }

            // How close to walk: the reach of the longest ready attack (ranged characters stay at range), melee range otherwise
            private float GetApproachRange(Agent agent, TimeSpan now)
            {
                float range = 0f;
                foreach (PowerInfo info in _powers)
                {
                    if (info.Kind != Kind.Attack || info.IsSelfCast || IsReady(agent, info, now, out Power power) == false)
                        continue;

                    range = Math.Max(range, power.GetRange());
                }

                return Math.Max(range * 0.8f, MeleeChaseRange);
            }

            // Sum of the damage bonuses the agent currently has for a power or for one of its keywords
            private static float GetDamageBonus(Agent agent, PowerPrototype proto)
            {
                PropertyCollection properties = agent.Properties;
                float bonus = 0f;

                foreach (var kvp in properties.IteratePropertyRange(PropertyEnum.DamagePctBonusForPower, proto.DataRef))
                    bonus += kvp.Value;

                foreach (var kvp in properties.IteratePropertyRange(PropertyEnum.DamageMultForPower, proto.DataRef))
                    bonus += kvp.Value;

                bonus += KeywordBonus(properties, PropertyEnum.DamagePctBonusForPowerKeyword, proto);
                bonus += KeywordBonus(properties, PropertyEnum.DamageMultForPowerKeyword, proto);
                return Math.Max(bonus, 0f);
            }

            private static float KeywordBonus(PropertyCollection properties, PropertyEnum propertyEnum, PowerPrototype proto)
            {
                float bonus = 0f;
                foreach (var kvp in properties.IteratePropertyRange(propertyEnum))
                {
                    Property.FromParam(kvp.Key, 0, out PrototypeId keywordRef);
                    KeywordPrototype keywordProto = keywordRef.As<KeywordPrototype>();
                    if (keywordProto != null && proto.HasKeyword(keywordProto))
                        bonus += kvp.Value;
                }

                return bonus;
            }

            // True if the entity has a condition that this power, used by this agent, put on it
            private static bool HasConditionFrom(WorldEntity entity, PrototypeId powerRef, ulong creatorId)
            {
                ConditionCollection conditions = entity?.ConditionCollection;
                if (conditions == null)
                    return false;

                foreach (Condition condition in conditions)
                {
                    if (condition.CreatorPowerPrototypeRef == powerRef && condition.CreatorId == creatorId)
                        return true;
                }

                return false;
            }

            #endregion

            #region Acting

            private bool Use(Agent agent, PowerInfo info, Power power, WorldEntity target, TimeSpan now, string reason, float score)
            {
                ulong targetId = target.Id;
                Vector3 targetPosition = target.RegionLocation.Position;

                if (agent.CanActivatePower(power, targetId, targetPosition) != PowerUseResult.Success)
                    return false;

                PowerActivationSettings settings = new(targetId, targetPosition, agent.RegionLocation.Position);
                settings.Flags |= PowerActivationSettingsFlags.NotifyOwner;
                if (agent.ActivatePower(info.Ref, ref settings) != PowerUseResult.Success)
                    return false;

                if (power.IsPartOfAMovementPower() == false)
                    agent.Locomotor?.Stop();

                TimeSpan cooldown = Power.GetCooldownDuration(info.Proto, agent, power.Properties);
                TimeSpan minRecast = TimeSpan.FromSeconds(info.MinRecastSeconds);
                info.ReadyTime = now + (cooldown > minRecast ? cooldown : minRecast);
                info.BonusTime = now + TimeSpan.FromMilliseconds(BonusRepeatMS);
                _nextPowerTime = now + TimeSpan.FromSeconds(_options.AttackDelaySeconds);
                _powerStartTime = now;
                _lastPower = info.Ref;

                if (_options.LogDecisions)
                    Logger.Info($"[CombatAI] [{agent.PrototypeName}] uses {info.Proto} on [{target.PrototypeName}]: {reason}" + (score > 0f ? $" (score {score:0.#})" : ""));

                return true;
            }

            // Walks toward an entity at the agent's speed (its run speed times MoveSpeedScale)
            private void Follow(Agent agent, ulong entityId, float range)
            {
                Locomotor locomotor = agent.Locomotor;
                if (locomotor == null)
                    return;

                LocomotionOptions locomotionOptions = Locomotor.DefaultFollowEntityLocomotionOptions;
                locomotionOptions.BaseMoveSpeed = locomotor.DefaultRunSpeed * Math.Max(_options.MoveSpeedScale, 0.1f);
                locomotor.FollowEntity(entityId, range, ref locomotionOptions);
            }

            #endregion
        }
    }
}
