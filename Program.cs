using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OBSWebsocketDotNet;

namespace Auto_Recorder
{
    static class Program
    {
        [DllImport("kernel32")]
        private static extern int OpenProcess(int dwDesiredAccess, int bInheritHandle, int dwProcessId);

        [DllImport("kernel32")]
        private static extern bool WriteProcessMemory(int hProcess, int lpBaseAddress, byte[] lpBuffer, int nSize, int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(int hProcess, int lpBaseAddress, byte[] lpBuffer, int dwSize, int lpNumberOfBytesRead);

        static Process[] processes;
        static int handle = 0;
        static OBSWebsocket obs = new OBSWebsocket();
        static byte raceState;
        static byte pRaceState;
        static byte loadState;
        static byte pLoadState;
        static bool recording;

        static MainForm MainForm = new MainForm();

        static void Main()
        {
            obs.RecordingStateChanged += Obs_RecordingStateChanged;
            obs.WSTimeout = TimeSpan.FromSeconds(2);

            Task task = new Task(() => AutoRecordTask());
            task.Start();

            Application.EnableVisualStyles();
            Application.Run(MainForm);
        }

        private static void AutoRecordTask()
        {
            while (true)
            {
                if (!obs.IsConnected)
                    try
                    {
                        obs.Connect("ws://127.0.0.1:4444", "");
                        recording = obs.GetStreamingStatus().IsRecording;
                    }
                    catch
                    { }

                processes = Process.GetProcessesByName("ASN_App_PcDx9_Final");

                if (processes.Length == 0)
                {
                    handle = 0;
                    if (recording)
                        ToggleRecording();
                }
                else if (handle == 0)
                {
                    Thread.Sleep(2000);
                    handle = OpenProcess(0x38, 0, processes[0].Id);
                    Initialise();
                }

                if (handle != 0)
                {
                    pRaceState = raceState;
                    raceState = ReadByte(0xFF0FFC);
                    pLoadState = loadState;
                    loadState = GetLoadState();

                    if (recording)
                    {
                        if (raceState != 18 && pRaceState == 18)
                            ToggleRecording();

                        if (raceState != 19 && pRaceState == 19)
                            ToggleRecording();

                        if (raceState != 22 && pRaceState == 22)
                            ToggleRecording();

                        if (raceState != 9 && raceState != 11 && pRaceState == 9)
                            ToggleRecording();

                        if (loadState == 0)
                            ToggleRecording();

                    }
                    else if (raceState == 8 && pRaceState != 8)
                    {
                        Thread.Sleep(370);
                        ToggleRecording();
                    }
                    else if (raceState == 9 && loadState == 2)
                        ToggleRecording();
                }

                if (obs.IsConnected)
                    MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label1.Text = "Current Status: Auto recording!"; });
                else
                    MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label1.Text = "Current Status: Not auto recording (could not find OBS)"; });

                Thread.Sleep(10);
            }
        }



        private static void Obs_RecordingStateChanged(OBSWebsocket sender, OutputState type)
        {
            recording = (type == OutputState.Starting || type == OutputState.Started);
        }

        static void ToggleRecording()
        {
            if (obs.IsConnected)
                obs.ToggleRecording();
        }

        static void Initialise()
        {
            WriteProcessMemory(handle, 0x70665C, new byte[] { 0xEB, 0x28 }, 2, 0);
            WriteProcessMemory(handle, 0x706686, new byte[] { 0x8B, 0xF1, 0x89, 0x3D, 0xFC, 0x0F, 0xFF, 0x00, 0xEB, 0xCE }, 10, 0);
        }

        static byte[] ReadBytes(int Address, int ByteCount)
        {
            byte[] Bytes = new byte[ByteCount];
            ReadProcessMemory(handle, Address, Bytes, ByteCount, 0);
            return Bytes;
        }

        static byte ReadByte(int Address)
        {
            return ReadBytes(Address, 1)[0];
        }

        static int ReadInteger(int Address)
        {
            return BitConverter.ToInt32(ReadBytes(Address, 4), 0);
        }

        static byte GetLoadState()
        {
            int tmp;

            tmp = ReadInteger(0xBCE92C);

            if (tmp == 0)
                return 0;

            tmp = ReadInteger(tmp + 4);
            tmp = ReadInteger(tmp);
            tmp = ReadInteger(tmp + 0xD400);

            if (tmp == 0)
                return 1;

            return 2;
        }
    }
}
