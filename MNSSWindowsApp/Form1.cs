using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using MNSSSocketManager;
using System.Configuration;
using System.Data.SqlClient;
using Common;

namespace MNSSWindowsApp {
    public partial class Form1 : Form, ReceivesSocketManagerMessages {
        private SocketManager mSocketManager = null;
        public Form1() {
            InitializeComponent();
            tbPort.Text= ConfigurationSettings.AppSettings["SocketListenerPort"];
            tbServerName.Text = ConfigurationSettings.AppSettings["ServerName"];
            tbServerNameMasseurs.Text = ConfigurationSettings.AppSettings["MasseursServerName"];
            SqlCommand cmd = new SqlCommand("uspSetAllPortsAndURLs");
            cmd.Parameters.Add("@Port", SqlDbType.Int).Value = tbPort.Text;
            cmd.Parameters.Add("@URL", SqlDbType.VarChar).Value = tbServerNameMasseurs.Text;
            Utils.executeNonQuery(cmd, ConnectionString);

        }
        public void heresMyPort(int port) {
            this.Invoke((MethodInvoker)delegate {
                tbPort.Text = port.ToString(); // runs on UI thread
            });
        }
        public void iReceivedThisDatums(string datums) {
            this.Invoke((MethodInvoker)delegate {
                List<string> lines = tbItems.Lines.ToList<String>();
                lines.Add(datums);
                tbItems.Lines = lines.ToArray<string>();
            });
        }
        public void heresMyServer(String server) {
            this.Invoke((MethodInvoker)delegate {
               tbServerName.Text = server; // runs on UI thread
            });
        }

        private void btnStop_Click(object sender, EventArgs e) {
            if (mSocketManager != null) {
                mSocketManager.Stop();
            }
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            SqlCommand cmd = new SqlCommand("uspSetAllPortsAndURLs");
            cmd.Parameters.Add("@Port", SqlDbType.Int).Value = tbPort.Text;
            cmd.Parameters.Add("@URL", SqlDbType.VarChar).Value = tbServerNameMasseurs.Text;
            Utils.executeNonQuery(cmd, ConnectionString);

        }

        private void btnStart_Click(object sender, EventArgs e) {
            if (tbServerName.Text.Trim() == String.Empty) {
                mSocketManager = new SocketManager(Convert.ToInt32(tbPort.Text),this,ConnectionString);
            }
            else {
                mSocketManager = new SocketManager(Convert.ToInt32(tbPort.Text), tbServerName.Text, this, ConnectionString);
            }
            mSocketManager.Start();
            btnStart.Enabled = false;
            btnStop.Enabled = true;
        }
        private string ConnectionString {
            get {
                ConnectionStringSettings settings =
                    ConfigurationManager.ConnectionStrings[ConfigurationManager.AppSettings["MassageNearby"]];
                return settings.ConnectionString;
            }
        }
    }
}
