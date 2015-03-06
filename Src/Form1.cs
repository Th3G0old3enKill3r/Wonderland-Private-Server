﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using System.IO;

namespace Wonderland_Private_Server
{
    public partial class Form1 : Form
    {
        bool blockclose = true;
          

        public Form1()
        {
            DebugSystem.Initialize();
            #region Initialize Objects           
                    
            //cGlobal.WLO_World = new Server.WloWorldNode();
            //cGlobal.gCharacterDataBase = new DataManagement.DataBase.CharacterDataBase();
            //cGlobal.gEveManager = new DataManagement.DataFiles.EveManager();
            //cGlobal.gGameDataBase = new DataManagement.DataBase.GameDataBase();
            //cGlobal.gItemManager = new DataManagement.DataFiles.ItemManager();
            //cGlobal.gSkillManager = new DataManagement.DataFiles.SkillDataFile();
            //cGlobal.gCompoundDat = new DataManagement.DataFiles.cCompound2Dat();
            //cGlobal.gUserDataBase = new UserDataBase();
            //cGlobal.gNpcManager = new DataManagement.DataFiles.NpcDat();
            //cGlobal.ApplicationTasks = new Utilities.Task.TaskManager();
            #endregion
            InitializeComponent();
        }

        void DebugSystem_onNewLog(object sender, DebugItem j)
        {
            new Task(() =>
            {
                this.Invoke(new Action(() =>
                        {
                switch (j.Type)
                {
                    case DebugItemType.Info_Light: SystemLog.AppendText(j.Msg+ "\r\n=============================\r\n"); break;
                    case DebugItemType.Network_Light: NetWorkLog.AppendText(j.Msg + "\r\n=============================\r\n"); break;
                    case DebugItemType.Error: errorLog.AppendText(j.Msg + "\r\n=============================\r\n"); break;
                    //case Utilities.LogType.DB: errorLog.AppendText(j.Msg + "\r\n=============================\r\n"); break;
                    //case Utilities.LogType.THRD: SystemLog.AppendText(j.Msg + "\r\n=============================\r\n"); break;
                    //case Utilities.LogType.UPDT: SystemLog.AppendText(j.Msg + "\r\n=============================\r\n"); break;
                }
                        }));
            }
            ).Start();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            Thread MainThread = new Thread(new ThreadStart(MainThreadWork));
            MainThread.IsBackground = true;
            MainThread.Init();
        }
        
        void GuiThread()
        {
            do
            {
                #region LogGUI
                string s = null;
                if (!string.IsNullOrEmpty((s = DebugSystem.PullLogItem())))
                    SystemLog.AppendText(s);
                #endregion
                #region TaskGui

                if (cGlobal.ApplicationTasks != null)
                    try
                    {
                        this.BeginInvoke(new Action(() => { cGlobal.ApplicationTasks.onUpdateGuiTick(); }));
                    }
                    catch { }
                    
                #endregion
                #region Form.System.Status gui
                try
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        thrd_label.Text = string.Format("Thread Cnt - {0}", ThreadManager.Count);
                    }));
                }
                catch { }
                #endregion
                #region Form.Update
                try
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        DateTime tmp = (cGlobal.ApplicationTasks.TaskItems.Count(c => c.TaskName == "Updating Application") > 0) ? cGlobal.ApplicationTasks.TaskItems.Single(c => c.TaskName == "Updating Application").Createdat : new DateTime();

                        autoUpdt_label.Text = string.Format(tmp.Add(cGlobal.SrvSettings.Update.AutoUpdt_Schedule).ToString());
                    }));
                }
                catch { }
                #endregion
                Thread.Sleep(5);

            }
            while (true);
        }


        void MainThreadWork()
        {

            cGlobal.Run = true;
            DebugSystem.Write("Intializing Please Wait.....");             
            cGlobal.SrvSettings = new Server.Config.Settings();
            cGlobal.GClient = new GupdtSrv.gitClient();
            cGlobal.GClient.GitinfoUpdated += GClient_GitinfoUpdated;
            cGlobal.GClient.onError += GClient_onError;
            cGlobal.GClient.infopipe += GClient_infopipe;
            cGlobal.GClient.onNewUpdate += GClient_onNewUpdate; 

            #region load settings file
            
            DebugSystem.Write("Loading Settings File");
            if (System.IO.File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\PServer\\Config.settings.wlo"))
            {
                System.Xml.Serialization.XmlSerializer diskio = new System.Xml.Serialization.XmlSerializer(typeof(Server.Config.Settings));

                try
                {
                    using (StreamReader file = new StreamReader(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\PServer\\Config.settings.wlo"))
                        cGlobal.SrvSettings = (Server.Config.Settings)diskio.Deserialize(file);
                    DebugSystem.Write("Settings File loaded successfully");
                }
                catch { DebugSystem.Write("Settings File failed to load"); }
            }
            else
                DebugSystem.Write("Settings File not found");

            if (GitBranch.Text != cGlobal.SrvSettings.Update.GitBranch)
            {
                cGlobal.GClient.Branch = cGlobal.SrvSettings.Update.GitBranch;
                GitBranch.Text = cGlobal.SrvSettings.Update.GitBranch;
            }

            if (GitUptOption.SelectedIndex != (byte)cGlobal.SrvSettings.Update.UpdtControl)
                GitUptOption.SelectedIndex = (byte)cGlobal.SrvSettings.Update.UpdtControl;

            #endregion
            

            #region Intialize Base Threads
            DebugSystem.Write("Intializing Gui Thread");
            Thread tmp2 = new Thread(new ThreadStart(GuiThread));
            tmp2.Name = "Gui Thread";
            tmp2.Init();

            #endregion

            #region intial check  if theres an update from github
            DebugSystem.Write("Checking For Update on Git...");
            cGlobal.GClient.CheckFor_Update();
            Thread.Sleep(2000);

            if (cGlobal.SrvSettings.Update.UpdtControl != Server.Config.UpdtSetting.Never && cGlobal.ApplicationTasks.TaskItems.Count(c => c.TaskName == "Updating Application") > 0)
                goto ShutDwn;
            #endregion

            

            #region Setup Form
            this.Invoke(new Action(() =>
            {
                dataGridView1.Columns[0].DataPropertyName = "TaskName";
                dataGridView1.Columns[1].DataPropertyName = "Interval";
                dataGridView1.Columns[2].DataPropertyName = "LastExecution";
                dataGridView1.Columns[3].DataPropertyName = "NextExecution";
                dataGridView1.Columns[4].DataPropertyName = "Status";
                dataGridView1.DataSource = cGlobal.ApplicationTasks.TaskItems;
            }));
            #endregion

            #region DataBase Initialization
            


            DebugSystem.Write("Verifying DataBase Authencation Setup");
            if (cGlobal.gUserDataBase.TestConnection())
            {
                DebugSystem.Write("Connection Successful");
                DebugSystem.Write("Verifying DataBase Tables");
                cGlobal.gUserDataBase.VerifySetup();
                cGlobal.gCharacterDataBase.VerifySetup();
                cGlobal.gGameDataBase.VerifySetup();
            }
            else
            {
                DebugSystem.Write("Connection not successful\r\n unable to start server");
                return;
            }

           


            #endregion

            #region Load Data Files
            cGlobal.gItemManager.LoadItems("Data\\Item.dat");
            cGlobal.gSkillManager.LoadSkills("Data\\Skill.dat");
            cGlobal.gNpcManager.LoadNpc("Data\\Npc.dat");
            cGlobal.gEveManager.LoadFile("Data\\eve.Emg");
            cGlobal.gCompoundDat.Load("Data\\Compound.dat");
            cGlobal.gCompoundDat.Load("Data\\Compound2.dat", false);

            #endregion

            #region Initialize the Wonderland Server
            DebugSystem.Write("Jump Starting Server...");
            cGlobal.WLO_World.Initialize();
            Thread.Sleep(2);
            cGlobal.TcpListener.Initialize();
            #endregion


            do
            {
                #region Thread Management
                //try
                //{
                //    Thread r;
                //    foreach (var t in ThreadManager. cGlobal.ThreadManager)
                //        if (!t.Value.IsAlive)
                //            if (cGlobal.ThreadManager.TryRemove(t.Key, out r))
                //                DebugSystem.Write(t.Value.Name + " has been Terminated", Utilities.LogType.THRD);
                //}
                //catch { }
                #endregion
                #region TaskManager
                cGlobal.ApplicationTasks.onUpdateTick();
                #endregion
                Thread.Sleep(1);
            }
            while (cGlobal.Run);

        ShutDwn:

            this.Invoke(new Action(() => { this.Enabled = false; }));

            UI.ShutDown_Dialog tmp = new UI.ShutDown_Dialog();
            tmp.Location = this.Location;
            tmp.Left = this.Left + 100;
            tmp.Top = this.Top + 250;

            tmp.ShowDialog();
            tmp.Dispose();

            cGlobal.Run = false;
            blockclose = false;
            this.Invoke(new Action(() => { Close(); }));

        }


        #region Git Client Events
        void GClient_onNewUpdate(object sender, Octokit.Release e)
        {
            switch ((Server.Config.UpdtSetting)GitUptOption.SelectedIndex)
            {
                case Server.Config.UpdtSetting.Auto:
                    {
                        DateTime tomorrow = new DateTime(DateTime.Now.Year, DateTime.Now.AddDays(1).Month, DateTime.Now.AddDays(1).Day, 1, 0, 0);
                        cGlobal.ApplicationTasks.CreateTask("Updating Application", tomorrow - DateTime.Now);
                    } break;
                case Server.Config.UpdtSetting.AutoandForce:
                    {

                    } break;
            }
            //cGlobal.ApplicationTasks.CreateTask()
        }
        void GClient_infopipe(object sender, string e)
        {
            DebugSystem.Write("GitUpdate: " + e);
        }
        void GClient_onError(object sender, Exception e)
        {
            DebugSystem.Write(new ExceptionData(e));
        }

        void GClient_GitinfoUpdated(object sender, EventArgs e)
        {
            try
            {
                this.BeginInvoke(new Action(() =>
                {
                    UpdatePane.Controls.Clear();
                    var list = cGlobal.GClient.ReleasesFnd;

                    foreach (var y in list.Where(c=>c.TagName != null).OrderByDescending(c => new Version(c.TagName)))
                        UpdatePane.Controls.Add(new UI.GitUpdateItem(cGlobal.GClient.myVersion, y));

                }));
            }
            catch { }

        }
        #endregion

        #region Form Events
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (cGlobal.Run || blockclose)
                e.Cancel = true;
            cGlobal.Run = false;
        }
        private void dataGridView1_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || (e.ColumnIndex !=
           dataGridView1.Columns["canceltask"].Index && e.ColumnIndex !=
           dataGridView1.Columns["retrytask"].Index)) return;

            if (e.ColumnIndex == dataGridView1.Columns["canceltask"].Index)
                cGlobal.ApplicationTasks.TaskItems[e.RowIndex].onCancel();
            else if (e.ColumnIndex == dataGridView1.Columns["retrytask"].Index)
                cGlobal.ApplicationTasks.TaskItems[e.RowIndex].onRetry();
        }

        #region Update Section
        private void GitBranch_TextChanged(object sender, EventArgs e)
        {
            if (cGlobal.GClient != null && cGlobal.GClient.Branch != GitBranch.Text)
            {
                cGlobal.SrvSettings.Update.GitBranch = GitBranch.Text;
                cGlobal.GClient.Branch = GitBranch.Text;
            }
        }
        private void GitUptOption_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cGlobal.SrvSettings.Update.UpdtControl != (Server.Config.UpdtSetting)GitUptOption.SelectedIndex)
                cGlobal.SrvSettings.Update.UpdtControl = (Server.Config.UpdtSetting)GitUptOption.SelectedIndex;

            if ((Server.Config.UpdtSetting)GitUptOption.SelectedIndex == Server.Config.UpdtSetting.Never)
                cGlobal.ApplicationTasks.EndTask("Application Update");
            else if ((Server.Config.UpdtSetting)GitUptOption.SelectedIndex != Server.Config.UpdtSetting.Never)
                cGlobal.ApplicationTasks.CreateTask("Application Update", cGlobal.SrvSettings.Update.UpdtChk_Interval);
        }
        #endregion

        private void updtrefresh_ValueChanged(object sender, EventArgs e)
        {
            if (cGlobal.SrvSettings.Update.UpdtChk_Interval.Minutes != updtrefresh.Value)
                cGlobal.SrvSettings.Update.UpdtChk_Interval = new TimeSpan(0,(int)updtrefresh.Value,0);

            if ((Server.Config.UpdtSetting)GitUptOption.SelectedIndex != Server.Config.UpdtSetting.Never)
                cGlobal.ApplicationTasks.ChangeInterval("Application Update", cGlobal.SrvSettings.Update.UpdtChk_Interval);
        }
        private void autoUpdt_Hr_ValueChanged(object sender, EventArgs e)
        {
            cGlobal.SrvSettings.Update.AutoUpdt_Schedule = new TimeSpan((int)autoUpdt_Hr.Value, (int)autoUpdt_Min.Value, 0);
        }
        private void autoUpdt_Min_ValueChanged(object sender, EventArgs e)
        {
            cGlobal.SrvSettings.Update.AutoUpdt_Schedule = new TimeSpan((int)autoUpdt_Hr.Value, (int)autoUpdt_Min.Value, 0);
        }
        
        #endregion


        

    }
}
