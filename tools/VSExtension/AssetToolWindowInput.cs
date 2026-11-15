using System.Windows.Input;
using System.Windows.Forms;
namespace CSharpGameEngine.VSExtension;
internal static class AssetToolWindowInput {
    internal static Key Shortcut(ref Message Message){
        if(Message.Msg!=0x0100||Control.ModifierKeys!=Keys.Control){ return Key.None; }
        return Message.WParam.ToInt32() switch{(int)Keys.Z=>Key.Z,(int)Keys.Y=>Key.Y,(int)Keys.S=>Key.S,_=>Key.None};
    }
}
