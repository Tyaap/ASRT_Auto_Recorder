using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using OBSWebsocketDotNet;
using MemoryUtil.ComponentUtil;
using System.Reflection;
using Microsoft.VisualBasic;

namespace Auto_Recorder
{
    class MainProgram
    {
        public static readonly MainForm MainForm = new MainForm();
        private static readonly OBSWebsocket OBS = new OBSWebsocket();
        private static readonly GameManager game = new GameManager();
        private static readonly System.Timers.Timer update_timer = new System.Timers.Timer();

        private static EventHandler OnStartRecording;
        private static EventHandler OnStopRecording;

        private static void Main()
        {
            Application.EnableVisualStyles();
            if (Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Length > 1)
            {
                MessageBox.Show("Another instance of this program is already running.\nOnly one instance of this application is allowed.", "ASRT Auto Recorder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(0);
            }

            OnStartRecording += StartRecording;
            OnStopRecording += StopRecording;

            OBS.WSTimeout = TimeSpan.FromMilliseconds(500);
            update_timer.AutoReset = false;
            update_timer.Interval = 15;
            update_timer.Elapsed += AutoRecordTask;
            update_timer.Start();
            Application.Run(MainForm);
        }

        private static void AutoRecordTask(object sender, EventArgs e)
        {
            // Check if connected to the game
            if (!game.IsHooked()) game.HookGameProcess();

            // Check again if connected. If not, exit the task
            if (!game.IsHooked())
            {
                OBSMessages.FailedToHookGameProcess();
                OBSMessages.NotRecording();
                update_timer.Start();
                return;
            }

            // Now try to connect to OBS
            if (!OBS.IsConnected) ConnectToOBS();

            // And recheck if connected
            if (!OBS.IsConnected)
            {
                OBSMessages.FailedToHookOBS();
                OBSMessages.NotRecording();
                update_timer.Start();
                return;
            }

            // Now check for OBS auth
            CheckOBSAuth();
            if (!OBS.IsConnected)
            {
                OBSMessages.FailedToHookOBS();
                OBSMessages.NotRecording();
                update_timer.Start();
                return;
            }

            // At this point both OBS and the game should have been hooked
            OBSMessages.AutoRecordingEnabled();

            // Launch the main update logic
            UpdateLogic();
            UpdateStrings();

            update_timer.Start();
        }

        private static void UpdateLogic()
        {
            game.UpdateMemoryWatchers();
            switch (OBS.GetStreamingStatus().IsRecording)
            {
                case true:
                    if (!game.watchers.LoadState.Current)
                    {
                        OnStopRecording?.Invoke(null, EventArgs.Empty);
                    }
                    else if (game.watchers.RaceState.Changed && (game.watchers.RaceState.Old == 18 || game.watchers.RaceState.Old == 19 || game.watchers.RaceState.Old == 22))
                    {
                        OnStopRecording?.Invoke(null, EventArgs.Empty);
                    }
                    else if (game.watchers.RaceState.Current != 9 && game.watchers.RaceState.Current != 11 && game.watchers.RaceState.Old == 9)
                    {
                        OnStopRecording?.Invoke(null, EventArgs.Empty);
                    }
                    break;
                case false:
                    if (game.watchers.LoadState.Current && game.watchers.RaceState.Current == 9)
                    {
                        OnStartRecording?.Invoke(null, EventArgs.Empty);
                    }
                    else if (game.watchers.RaceState.Current == 8 && game.watchers.RaceState.Changed)
                    {
                        OnStartRecording?.Invoke(null, EventArgs.Empty);
                    }
                    break;
            }
        }

        private static void UpdateStrings()
        {
            switch (OBS.GetStreamingStatus().IsRecording)
            {
                case true: OBSMessages.Recording(); break;
                case false: OBSMessages.NotRecording(); break;
            }
        }

        private static void ConnectToOBS()
        {
            try { OBS.Connect("ws://127.0.0.1:4444", ""); } catch { }
        }

        private static void CheckOBSAuth()
        {
            bool AuthConnected = false;
            do
            {
                try
                {
                    OBS.GetStreamingStatus();
                    AuthConnected = true;
                }
                catch
                {
                    var question = Interaction.InputBox("OBS WebSocket requires a password.\n\nPlease inpput the password and click OK\nor click \"Cancel\" to exit the program.", "TSR Auto Recorder", "password");
                    if (question == "") Environment.Exit(0);
                    if (!OBS.IsConnected) break;
                    try { OBS.Authenticate(question, OBS.GetAuthInfo()); } catch (AuthFailureException) { }
                }
            } while (!AuthConnected);
        }

        private static void StartRecording(object sender, EventArgs e)
        {
            if (!OBS.GetRecordingStatus().IsRecording) OBS.StartRecording();
        }

        private static void StopRecording(object sender, EventArgs e)
        {
            if (OBS.GetRecordingStatus().IsRecording) OBS.StopRecording();
        }
    }

    static class OBSMessages
    {
        public static void FailedToHookGameProcess() { MainProgram.MainForm.BeginInvoke((MethodInvoker)delegate () { MainProgram.MainForm.label1.Text = "Auto recording disabled: couldn't connect to the game!"; }); }
        public static void FailedToHookOBS() { MainProgram.MainForm.BeginInvoke((MethodInvoker)delegate () { MainProgram.MainForm.label1.Text = "Auto recording disabled: couldn't connect to OBS!"; }); }
        public static void AutoRecordingEnabled() { MainProgram.MainForm.BeginInvoke((MethodInvoker)delegate () { MainProgram.MainForm.label1.Text = "Auto recording enabled!"; }); }
        public static void NotRecording() { MainProgram.MainForm.BeginInvoke((MethodInvoker)delegate () { MainProgram.MainForm.labelStatus.Text = "Currently not recording"; }); }
        public static void Recording() { MainProgram.MainForm.BeginInvoke((MethodInvoker)delegate () { MainProgram.MainForm.labelStatus.Text = "Currently recording!"; }); }

    }

    class GameManager
    {
        private Process game;
        internal Watchers watchers;

        public bool IsHooked()
        {
            return !(game == null || game.HasExited);
        }

        public void HookGameProcess()
        {
            game = Process.GetProcessesByName("ASN_App_PcDx9_Final").OrderByDescending(x => x.StartTime).FirstOrDefault(x => !x.HasExited);
            if (game == null) return;
            try
            {
                game.WriteBytes(game.MainModuleWow64Safe().BaseAddress + 0x306686, new byte[] { 0x8B, 0xF1, 0x89, 0x3D, 0xFC, 0x0F, 0xFF, 0x00, 0xEB, 0xCE });
                game.WriteBytes(game.MainModuleWow64Safe().BaseAddress + 0x30665C, new byte[] { 0xEB, 0x28 });
                watchers = new Watchers(game);
            }
            catch
            {
                game = null;
            }
        }

        internal class Watchers : MemoryWatcherList
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
        
        public void UpdateMemoryWatchers()
        {
            this.watchers.UpdateAll(game);
        }
    }
}