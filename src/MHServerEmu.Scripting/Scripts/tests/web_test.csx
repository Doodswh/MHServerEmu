// Portal test: answers the buttons on the portal test page (Data/Web/Portal/index.html), which the client shows in its
// built-in browser. Proves the round trip: web page -> web frontend -> player manager -> this game instance -> this script
// -> back to the page.
//
// To see the page in game, point one of these at the portal in Config.ini ([MTXStore] section) and restart:
//   RealMoneyUrl=<your web frontend address>/Portal     the store's Add G button opens it (the client sends the session token)
//   HomePageUrl=<your web frontend address>/Portal      the store's home tab shows it (the page tells you whether a token came)
//
//   !portal        opens the store window from the server (what an NPC click would do)
//
// Actions (the page sends a name and a text; e.Reply(text) answers and marks the action as handled):
//   test.whoami    your player name, hero, level and prestige
//   test.banner    shows a banner on your screen
//   test.chat      sends the page's text to your chat window
//
// NOTE: on-screen text registered here reaches players the next time they connect (client limitation).

using MHServerEmu.Games.Entities.Avatars;

ScriptText.Register("web_test_banner", "Hello from the web page!");

Hooks.On(ScriptHooks.WebAction, e =>
{
    // Every script sees every action: only answer your own names
    if (e.Action.StartsWith("test.") == false)
        return;

    Player player = e.Player;
    Avatar avatar = player.CurrentAvatar;

    switch (e.Action)
    {
        case "test.whoami":
            e.Reply(avatar == null
                ? $"Player {player.GetName()}, no hero in the world right now."
                : $"Player {player.GetName()}, playing {avatar.PrototypeName} (level {avatar.CharacterLevel}, prestige {avatar.PrestigeLevel}) " +
                  $"in {avatar.Region?.PrototypeName ?? "no region"}.");
            break;

        case "test.banner":
            ScriptText.ShowBanner(player, "web_test_banner", 0, "reward", 3500);
            e.Reply("Banner sent. Look at the game (it only shows if you logged in after this script was loaded).");
            break;

        case "test.chat":
            // Text from a web page is untrusted: trim it and keep it short
            string text = e.Data.Trim();
            if (text.Length > 120)
                text = text.Substring(0, 120);

            ScriptHooks.SendChatMessage(player, "[Portal] " + (text.Length > 0 ? text : "(empty message)"), false);
            e.Reply($"Sent {text.Length} characters to your chat window.");
            break;

        // anything else starting with "test." is left unhandled on purpose: the page shows "no script handles this action"
    }

    if (e.Handled)
        Log.Info($"Portal action [{e.Action}] from {player.GetName()}");
});

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "portal")
        return;

    e.Handled = true;
    e.Reply(ScriptPresentation.OpenPanel(e.Player, "StorePanel")
        ? "Asked your client to open the store window."
        : "Could not send the open-window message.");
});

Log.Info("Portal test loaded");
