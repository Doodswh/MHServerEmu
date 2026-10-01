using Gazillion;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.UI;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// Server-driven popups with one or two buttons (the same dialog the game uses for multi-destination portals).
    /// All text is registered text keys (ScriptText.Register), so new text shows after a reconnect.
    /// </summary>
    public static class ScriptDialog
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        /// <summary>
        /// Shows <paramref name="player"/> a popup with the <paramref name="messageKey"/> text and up to two buttons.
        /// <paramref name="onResult"/> gets 1 or 2 for the button clicked, or 0 if the popup was closed / cancelled.
        /// Pass an empty <paramref name="button2Key"/> for a single button. <paramref name="target"/> (optional) anchors the popup
        /// to an entity, like portal popups.
        /// </summary>
        public static bool Show(Player player, string messageKey, string button1Key, string button2Key, Action<Player, int> onResult,
            WorldEntity target = null)
        {
            if (player == null || player.Game == null)
                return false;

            LocaleStringId message = ScriptText.GetId(messageKey);
            LocaleStringId button1 = ScriptText.GetId(button1Key);
            if (message == LocaleStringId.Blank || button1 == LocaleStringId.Blank)
                return Logger.WarnReturn(false, $"Show(): Unregistered text key [{messageKey}] or [{button1Key}]");

            Game game = player.Game;
            GameDialogInstance dialog = game.GameDialogManager.CreateInstance(player.DatabaseUniqueId);
            dialog.Message.LocaleString = message;
            dialog.Options = DialogOptionEnum.ScreenBottom;

            if (target != null)
            {
                dialog.Options |= DialogOptionEnum.WorldClick;
                dialog.TargetId = target.Id;
                dialog.InteractorId = player.CurrentAvatar?.Id ?? 0;
            }

            dialog.AddButton(GameDialogResultEnum.eGDR_Option1, button1, ButtonStyle.SecondaryPositive);

            if (string.IsNullOrEmpty(button2Key) == false)
            {
                LocaleStringId button2 = ScriptText.GetId(button2Key);
                if (button2 != LocaleStringId.Blank)
                    dialog.AddButton(GameDialogResultEnum.eGDR_Option2, button2, ButtonStyle.SecondaryNegative);
            }

            dialog.OnResponse = (playerDbId, response) =>
            {
                Player responder = game.EntityManager.GetEntityByDbGuid<Player>(playerDbId);
                if (responder == null || onResult == null)
                    return;

                int button = response.ButtonIndex switch
                {
                    GameDialogResultEnum.eGDR_Option1 => 1,
                    GameDialogResultEnum.eGDR_Option2 => 2,
                    _ => 0,
                };

                try
                {
                    onResult(responder, button);
                }
                catch (Exception e)
                {
                    Logger.Error($"Show(): Popup callback failed: {e}");
                }
            };

            game.GameDialogManager.ShowDialog(dialog);
            return true;
        }
    }
}
