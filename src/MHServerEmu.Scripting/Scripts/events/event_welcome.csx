// Event welcome: a popup that tells players about the running events (here the Halloween event and the Halloween Candy Hunt
// community goal) the first time they log in while they run.
//
//  - The text is split over a few pages ("Next"). "Got it!" on the last page marks the popup as seen for that player (saved,
//    so it survives restarts). "Remind me later" or closing it shows it again at their next login.
//  - Change PopupId for the next event to show a new popup to everyone again.
//
//   !events   show the popup again
//
// NOTE: on-screen text registered here reaches players the next time they connect (client limitation).

using System.Collections.Concurrent;

//------------------------------------------------------------------------------
// Settings
//------------------------------------------------------------------------------

const bool   Enabled      = true;
const string PopupId      = "halloween_2026";   // saved as Data/ScriptData/welcome_<PopupId>.tsv; change it for a new event
DateTime?    EndsUtc      = null;               // stop showing after this (e.g. new DateTime(2026, 11, 3)); null = no end
const float  DelaySeconds = 5f;                 // after the player loads in, so the popup is not hidden by the loading screen

// The popup pages, shown one after another (the popup is too small for everything at once). Keep the numbers in step with
// events/community_goal.csx (Stages) and events/halloween.csx.
var Pages = new[]
{
    "#emphasis#HAPPY HALLOWEEN, HEROES!#/emphasis#\n\n" +
    "#emphasis#Trick or Treat#/emphasis#\n" +
    "Enemies everywhere drop Halloween Candy and Pumpkins. Every boss you defeat is a Trick or a Treat: " +
    "a Treat showers you with goodies, a Trick unleashes a Halloween villain and a pack of demons.\n\n" +
    "Collect 5 Treats for a Halloween Loot Explosion! In Avengers Tower, Ghost trades 50 Candy for a " +
    "Halloween Mystery Bag and Clea trades 50 Pumpkins for a Chest of 50 Event Currencies. " +
    "Type !halloween for your count.",

    "#emphasis#Community Goal: Halloween Candy Hunt#/emphasis#\n" +
    "Every piece of candy anyone picks up counts toward the server goal. Four reward stages unlock at " +
    "2,000 / 8,000 / 25,000 / 60,000 candy. Collect at least 50 candy yourself to claim them.\n\n" +
    "The final stage includes Rachel Alves and a level 75 Doomsaw!\n\n" +
    "Check the progress with Beast in Avengers Tower or type !goal. Type !events to see this again.",
};

//------------------------------------------------------------------------------
// State
//------------------------------------------------------------------------------

string StoreName = "welcome_" + PopupId;

var seen  = new ConcurrentDictionary<ulong, bool>(); // player db id => clicked "Got it!"
var shown = new ConcurrentDictionary<ulong, bool>(); // player entity id (new every login) => popup already shown this login

foreach (var kvp in ScriptStorage.Load(StoreName))
{
    if (ulong.TryParse(kvp.Key, out ulong playerId))
        seen[playerId] = true;
}

for (int i = 0; i < Pages.Length; i++)
    ScriptText.Register($"welcome_page_{i}", Pages[i]);
ScriptText.Register("welcome_next", "Next");
ScriptText.Register("welcome_ok", "Got it!");
ScriptText.Register("welcome_later", "Remind me later");

bool IsActive() => Enabled && (EndsUtc == null || DateTime.UtcNow <= EndsUtc);

void Save()
{
    ScriptStorage.Save(StoreName, seen.Keys.Select(id => new KeyValuePair<string, string>(id.ToString(), "1")).ToList());
}

// Every page but the last has "Next"; the last has "Got it!"
void ShowPopup(Player player, int page = 0)
{
    bool last = page >= Pages.Length - 1;
    ScriptDialog.Show(player, $"welcome_page_{page}", last ? "welcome_ok" : "welcome_next", "welcome_later", (responder, button) =>
    {
        if (button != 1)
            return;

        if (last == false)
            ShowPopup(responder, page + 1);
        else if (seen.TryAdd(responder.DatabaseUniqueId, true))
            Save();
    });
}

//------------------------------------------------------------------------------
// Hooks
//------------------------------------------------------------------------------

Hooks.On(ScriptHooks.PlayerEnteredRegion, e =>
{
    Player player = e.Player;
    ulong sessionId = player.Id;

    // Once per login (the player entity id is new every login), so moving between regions does not show it again
    if (IsActive() == false || seen.ContainsKey(player.DatabaseUniqueId) || shown.TryAdd(sessionId, true) == false)
        return;

    After(player.Game, DelaySeconds, () =>
    {
        if (player.IsDestroyed || player.CurrentAvatar == null || player.CurrentAvatar.IsInWorld == false)
        {
            shown.TryRemove(sessionId, out _);   // left or still loading: try again on their next region entry
            return;
        }

        ShowPopup(player);
    });
});

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "events")
        return;

    e.Handled = true;
    if (IsActive() == false)
    {
        e.Reply("No events are running right now.");
        return;
    }

    ShowPopup(e.Player);
});

Log.Info($"Event welcome loaded ({(IsActive() ? "on" : "off")}, popup [{PopupId}], {seen.Count} players have seen it)");
