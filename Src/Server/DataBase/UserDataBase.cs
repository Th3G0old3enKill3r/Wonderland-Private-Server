﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using System.Data;
using Game;
using System.Threading;

namespace System
{
    public enum GMStatus
    {
        None,
    }

    public class User
    {
        readonly object mlock = new object();

        DateTime imupdate; public DateTime last_imUpdate { get { lock (mlock)return imupdate; } set { lock (mlock)imupdate = value; } }
        string username, cipher;
        int databaseID;
        int im;
        int gmlvl; public int GMlvl { private get { return gmlvl; } set { lock (mlock)gmlvl = value; } }
        public GMStatus GMRank
        {
            get
            {
                lock (mlock)
                    return GMStatus.None;
            }
        }
        int character1ID, character2ID;

        public int Character1ID
        {
            get
            {
                lock (mlock) return character1ID;
            }
            set
            {
                lock (mlock) character1ID = value;
            }

        }
        public int Character2ID
        {
            get
            {
                lock (mlock) return character2ID;
            }
            set
            {
                lock (mlock) character2ID = value;
            }

        }
        public string Cipher
        {
            get { lock (mlock)return cipher; }
            set { lock (mlock)cipher = value; }
        }
        public string UserName
        {
            get { lock (mlock)return username; }
            set { lock (mlock)username = value; }
        }
        public int IM
        {
            get { lock (mlock)return im; }
            set { lock (mlock)im = value; }
        }
        public int DataBaseID
        {
            get { lock (mlock)return databaseID; }
            set { lock (mlock)databaseID = value; }
        }

        public void Clear()
        {
            imupdate = new DateTime();
            username = "";
            databaseID = im = GMlvl = character1ID = character2ID = 0;

        }

    }

    public sealed class UserDataBase:RCLibrary.Core.DataBase
    {

        //Used to provide flexibility to alter columns name and match them with the correct value
        const string TableName = "user";

        const string Username_Ref = "username";
        const string Password_Ref = "password";
        const string UserID_Ref = "userID";
        const string CharacterID1_Ref = "character1ID";
        const string CharacterID2_Ref = "character2ID";
        const string IM_Ref = "IM";
        const string GM_Ref = "GM";
        const string Char_Delete_Code_Ref = "char_delete_code";

        DataSet userTable;
        DateTime refresh;

        List<string> GOnlineUsers;
        List<Player> OnlineUsers;
        Queue<Player> DisconnectedPlayers = new Queue<Player>(50);

        readonly AsyncLock mlock = new AsyncLock();

        readonly ManualResetEvent dbupdate = new ManualResetEvent(true);

        bool shutdown = false;

        public UserDataBase()
        {
            DebugSystem.Write("Configuring Dataset.... Please wait");
            userTable = new DataSet("user");
            InitializeMysqlAdapter("select * from " + TableName);

            OnlineUsers = new List<Player>();
            GOnlineUsers = new List<string>();
            refresh = DateTime.Now.AddMinutes(10); 
        }

        public void VerifySetup()
        {
            //DataBase Setup
            Dictionary<string, string> col = new Dictionary<string, string>();
            col.Add(UserID_Ref, "int/NN/PK");
            col.Add(Username_Ref, "text");
            col.Add(Password_Ref, "text");
            col.Add(CharacterID1_Ref, "int/NN/DF 0");
            col.Add(CharacterID2_Ref, "int/NN/DF 0");
            col.Add(IM_Ref, "int/NN/DF 0");
            col.Add(GM_Ref, "int/NN/DF 0");
            col.Add(Char_Delete_Code_Ref, "text");

            #region UserDataBase table Verification
            DebugSystem.Write("Checking " + TableName + " table");
        retry:
            if (GetDataTable("SELECT * FROM " + TableName) != null) goto exist;

            DebugSystem.Write("Setuping up " + TableName + " table");

            string nonsqlite_prikey = "";
            string cmstr = "create table " + TableName + " (";

            foreach (var t in col)
            {
                var str = "";
                var att = t.Value.Split('/');

                switch (ServType)
                {
                    #region Mysql
                    case RCLibrary.Core.DataBaseTypes.MySQl:
                        {
                            foreach (var a in att)
                                switch (a)
                                {
                                    case "text": str += "text "; break;
                                    case "int": str += "int(11) "; break;
                                    case "NN": str += "NOT NULL "; break;
                                    case "PK": nonsqlite_prikey = "PRIMARY KEY (" + t.Key + ")"; break;
                                }
                        } break;
                    #endregion
                    #region Sqlite
                    case RCLibrary.Core.DataBaseTypes.Sqlite:
                        {
                            if (att.Count(c => c == "pk") > 0 && att.Count(c => c == "NN") > 0)
                                att = att.Where(c => c != "NN").ToArray();

                            foreach (var a in att)
                                switch (a.Split(' ')[0])
                                {
                                    case "text": str += "TEXT "; break;
                                    case "int": str += "INTEGER "; break;
                                    case "PK": str += "PRIMARY KEY "; break;
                                }
                        } break;
                    #endregion
                }

                cmstr += string.Format("{0} {1},", t.Key, str);
            }

            if (nonsqlite_prikey != "")
                cmstr += string.Format("{0},", nonsqlite_prikey);

            cmstr = cmstr.Substring(0, cmstr.Length - 1);

            if (ServType == RCLibrary.Core.DataBaseTypes.MySQl)
                cmstr += ") ENGINE=InnoDB DEFAULT CHARSET=utf8;";
            else if (ServType == RCLibrary.Core.DataBaseTypes.Sqlite)
                cmstr += ");";

            ExecuteNonQuery(cmstr);

        exist:

            //table exists verify columns  
            foreach (string h in col.Keys)
            {
                if (GetDataTable("select " + h + " from " + TableName) == null)
                {
                    DebugSystem.Write("Recreating " + TableName + " table");

                    ExecuteNonQuery("drop table if exists " + TableName);
                    goto retry;
                }
            }
            #endregion
        }

        public int Count() { return GOnlineUsers.Count + OnlineUsers.Count; }

        public bool isLoggedin(string user)
        {
            bool resp = (OnlineUsers.Count(c => c.UserAcc.UserName == user) > 0 || GOnlineUsers.Contains(user));
            DebugSystem.Write(DebugItemType.Info_Heavy, "Checking if User '{0}' is Online... [Resp]: {1}", DebugItemType.Info_Heavy, user, resp);
            return resp;
        }

        
        bool isValidAccount(string username)
        {
            List<KeyValuePair<string, string>> parameters = new List<KeyValuePair<string, string>>();
            parameters.Add(new KeyValuePair<string, string>("@user", username));

            return (cGlobal.gDataBaseConnection.GetDataTable("SELECT * FROM " + TableName + " where " + Username_Ref + " =@user", parameters.ToArray()).Rows.Count > 0);
        }
        public uint isValidAccount(string username, string pass)
        {
            uint rem = 0;
            
            DataRow[] rows;

            List<KeyValuePair<string, string>> parameters = new List<KeyValuePair<string, string>>();
            parameters.Add(new KeyValuePair<string, string>("@user", username));

            var table = cGlobal.gDataBaseConnection.GetDataTable("SELECT * FROM " + TableName + " where " + Username_Ref + " =@user", parameters.ToArray());

            if (table.Rows.Count > 0)
            {
                rows = new DataRow[table.Rows.Count];
                table.Rows.CopyTo(rows, 0);

                if (cGlobal.gDataBaseConnection.VerifyPassword(pass, rows[0][Password_Ref].ToString()))
                    uint.TryParse(rows[0][UserID_Ref].ToString(), out rem);
                else
                    rem = 0;
            }
            else
                rem = 0;
            
            return rem;
        }
        public bool Update_Player_ID(uint user, UInt32 id, byte slot)
        {
            if (user == 0) return false;

            string col = "";
            switch (slot)
            {
                case 1: { col = CharacterID1_Ref; } break;
                case 2: { col = CharacterID2_Ref; } break;
            }

            Dictionary<string,string> cols = new Dictionary<string,string>();
            cols.Add(col,id.ToString());

            try {cGlobal.gDataBaseConnection.Update(TableName,cols,UserID_Ref+" = '" + user + "'");}
            catch (MySqlException ex) { DebugSystem.Write(ex); return false; }

            return true;
        }
        public string[] GetUserData(uint user, string pass)
        {
            DataRow[] rows = new DataRow[0];

            List<KeyValuePair<string, string>> parameters = new List<KeyValuePair<string, string>>();
            parameters.Add(new KeyValuePair<string, string>("@id", user.ToString()));

            var src = cGlobal.gDataBaseConnection.GetDataTable("SELECT * FROM " + TableName + " WHERE "+UserID_Ref+" = @id", parameters.ToArray());

            if (src.Rows.Count > 0)
            {
                rows = new DataRow[src.Rows.Count];
                src.Rows.CopyTo(rows, 0);
                if (cGlobal.gDataBaseConnection.VerifyPassword(pass, rows[0][Password_Ref].ToString()))
                {
                    string ch = ""; string ch2 = "0";
                    if (rows[0][Char_Delete_Code_Ref] != DBNull.Value)
                        ch = rows[0][Char_Delete_Code_Ref].ToString();
                    if (rows[0][GM_Ref] != DBNull.Value)
                        ch2 = rows[0][GM_Ref].ToString();

                    return new string[] { rows[0][Username_Ref].ToString(), rows[0][CharacterID1_Ref].ToString(), rows[0][CharacterID2_Ref].ToString(), ch, ch2, rows[0][IM_Ref].ToString() };
                }
            }
            return null;
        }
        public int GetIMPoints(uint user)
        {
            DataTable src = null;
            DataRow[] rows = new DataRow[0];

            try {src = cGlobal.gDataBaseConnection.GetDataTable("SELECT * FROM "+TableName+" where "+UserID_Ref+" = '" + user + "'"
                ); }
            catch (MySqlException ex) { DebugSystem.Write(ex); return 0; }

            if (src.Rows.Count > 0)
            {
                rows = new DataRow[src.Rows.Count];
                src.Rows.CopyTo(rows, 0);
                return int.Parse(rows[0][IM_Ref].ToString());
            }
            return 0;
        }
        public bool UpdateUser(uint user, string delete = null, object im = null, object char1 = null, object char2 = null)
        {
            if (user == 0) return false;
            
            Dictionary<string, string> str = new Dictionary<string, string>();
            if (delete != null) str.Add(Char_Delete_Code_Ref, delete);
            if (im != null) str.Add(IM_Ref, im.ToString());
            if (char1 != null) str.Add(CharacterID1_Ref, char1.ToString());
            if (char2 != null) str.Add(CharacterID2_Ref, char2.ToString());

             try {cGlobal.gDataBaseConnection.Update(TableName, str,UserID_Ref+" = '" + user + "'");}
            catch (MySqlException ex) { DebugSystem.Write(ex); return false; }

            return true;
        }

    }

}
