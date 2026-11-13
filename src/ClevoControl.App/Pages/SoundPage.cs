using System.Windows;
using System.Windows.Controls;
using ClevoControl.Core;
using ClevoControl.Core.Services;

namespace ClevoControl.App.Pages;

public sealed class SoundPage(ControlCenter cc) : AppPage(cc)
{
    public override UIElement Build()
    {
        var root = new StackPanel();
        root.Children.Add(Ui.PageTitle(Loc.T("Sound", "Sound")));
        root.Children.Add(Ui.PageSubtitle(Loc.T("Lautstärke, Mikrofon und Ausgabegerät.", "Volume, microphone and output device.")));

        var snd = Cc.Sound;
        var s = new StackPanel();
        s.Children.Add(Ui.SliderRow(Loc.T("Lautstärke", "Volume"), 0, 100, snd.Volume ?? 0, v => snd.Volume = (int)v, v => $"{v:0} %"));
        s.Children.Add(Ui.Switch(Loc.T("Ton stumm", "Mute"), snd.Muted, v => snd.Muted = v));
        if (snd.HasMicrophone)
            s.Children.Add(Ui.Switch(Loc.T("Mikrofon stumm", "Mute microphone"), snd.MicrophoneMuted, v => snd.MicrophoneMuted = v,
                Loc.T("Tipp: Mit dem Effekt „On Air“ zeigt die Tastatur an, wenn eine App das Mikrofon nutzt.", "Tip: the “On Air” effect shows on the keyboard when an app uses the microphone.")));
        root.Children.Add(Ui.Card(s, Loc.T("Lautstärke", "Volume")));

        var devices = new WrapPanel();
        foreach (var d in snd.GetOutputDevices())
        {
            var dev = d;
            devices.Children.Add(Ui.Tile(Icons.Speaker, dev.Name, dev.IsDefault ? Loc.T("Standard", "Default") : null, dev.IsDefault, "audiodev", () =>
            {
                if (!SoundService.SetDefaultOutput(dev.Id))
                    MessageBox.Show(Loc.T("Ausgabegerät konnte nicht gewechselt werden.", "Could not switch the output device."), "ClevoControl");
            }, 260));
        }
        root.Children.Add(Ui.Card(devices, Loc.T("Ausgabegerät", "Output device")));
        return root;
    }
}
