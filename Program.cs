using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OBSWebsocketDotNet;
using MemoryUtil.ComponentUtil;
using Microsoft.VisualBasic;
using System.Linq;
using System.Reflection;

namespace Auto_Recorder
{
    static class Program
    {
        private static MainForm MainForm = new MainForm();
        private static OBSWebsocket obs = new OBSWebsocket();
        private static Process game;
        private static Watchers watchers;

        static void Main()
        {
            obs.WSTimeout = TimeSpan.FromSeconds(1);
            Task task = new Task(() => AutoRecordTask());
            task.Start();
            Application.EnableVisualStyles();
            Application.Run(MainForm);
        }

        private static void AutoRecordTask()
        {
            while (true)
            {
                Thread.Sleep(15);

                if (game == null || game.HasExited)
                {
                    if (!HookGameProcess())
                    {
                        MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label1.Text = "Auto recording disabled: couldn't connect to the game!"; });
                        continue;
                    }
                }

                if (!obs.IsConnected)
                {
                    try
                    {
                        obs.Connect("ws://127.0.0.1:4444", "");
                        MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label1.Text = "Auto recording enabled!"; });
                        continue;
                    }
                    catch
                    {
                        MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label1.Text = "Auto recording disabled: couldn't connect to OBS!"; });
                        continue;
                    }
                }

                try
                {
                    obs.GetStreamingStatus();
                }
                catch
                {
                    var question = Interaction.InputBox("OBS WebSocket requires a password.\n\nPlease inpput the password and click OK\nor click \"Cancel\" to exit the program.", "ASRT Auto Recorder", "password");

                    if (question == "") Environment.Exit(0);

                    try
                    {
                        obs.Authenticate(question, obs.GetAuthInfo());
                        MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label1.Text = "Auto recording enabled!"; });
                    }
                    catch (AuthFailureException)
                    {
                        obs.Disconnect();
                    }
                    continue;
                }

                watchers.UpdateAll(game);


                switch (obs.GetStreamingStatus().IsRecording)
                {
                    case true:
                        if (!watchers.LoadState.Current) {
                            StopRecording();
                        }
                        else if (watchers.RaceState.Changed && (watchers.RaceState.Old == 18 || watchers.RaceState.Old == 19 || watchers.RaceState.Old == 22))
                        {
                            StopRecording();
                        }
                        else if (watchers.RaceState.Current != 9 && watchers.RaceState.Current != 11 && watchers.RaceState.Old == 9)
                        {
                            StopRecording();
                        }
                        break;
                    case false:
                        if (watchers.LoadState.Current && watchers.RaceState.Current == 9)
                        {
                            StartRecording();
                        }
                        else if (watchers.RaceState.Current == 8 && watchers.RaceState.Changed)
                        {
                            StartRecording();
                        }
                        break;
                }

                UpdateString();
            }
        }

        private static void UpdateString()
        {
            switch (obs.IsConnected)
            {
                case true:
                    MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label1.Text = "Current Status: Auto recording!"; });
                    break;
                case false:
                    MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label1.Text = "Current Status: Not auto recording (could not find OBS)"; });
                    break;
            }
        }

        private static void StartRecording() { if (!obs.GetStreamingStatus().IsRecording) obs.StartRecording(); }

        private static void StopRecording() { if (obs.GetStreamingStatus().IsRecording) obs.StopRecording(); }

        private static bool HookGameProcess()
        {
            game = Process.GetProcessesByName("ASN_App_PcDx9_Final").OrderByDescending(x => x.StartTime).FirstOrDefault(x => !x.HasExited);
            if (game == null) return false;

            try
            {
                game.WriteBytes(game.MainModuleWow64Safe().BaseAddress + 0x306686, new byte[] { 0x8B, 0xF1, 0x89, 0x3D, 0xFC, 0x0F, 0xFF, 0x00, 0xEB, 0xCE });
                game.WriteBytes(game.MainModuleWow64Safe().BaseAddress + 0x30665C, new byte[] { 0xEB, 0x28 });
                watchers = new Watchers(game);
            }
            catch
            {
                game = null;
                return false;
            }
            return true;
        }
    }

    class Watchers : MemoryWatcherList
    {
        public MemoryWatcher<byte> RaceState { get; }
        public MemoryWatcher<bool> LoadState { get; }

        public Watchers(Process game)
        {
            this.RaceState = new MemoryWatcher<byte>(new DeepPointer(game.MainModuleWow64Safe().BaseAddress + 0xBF0FFC)) { FailAction = MemoryWatcher.ReadFailAction.SetZeroOrNull };
            this.LoadState = new MemoryWatcher<bool>(new DeepPointer(game.MainModuleWow64Safe().BaseAddress + 0x7CE92C, 0x4, 0x0, 0xD400)) { FailAction = MemoryWatcher.ReadFailAction.SetZeroOrNull };
            this.AddRange(this.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(p => !p.GetIndexParameters().Any()).Select(p => p.GetValue(this, null) as MemoryWatcher).Where(p => p != null));
        }
    }
}
