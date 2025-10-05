using MHServerEmu.Games.Entities; 
using MHServerEmu.Games.Events.Templates;

// Define a custom event to show a UI message to a player.
// It targets a 'Player' object and takes one string parameter (the message).
public class ShowUIMessageEvent : CallMethodEventParam1<Player, string>
{
    // This is the required override. It tells the event scheduler which
    // method to actually call when the event is triggered.
    protected override CallbackDelegate GetCallback()
    {
        // We're telling it to call the 'ShowUIMessage' method on the Player object
        return (target, message) => target.ShowUIMessage(message);
    }
}