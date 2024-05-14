using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Audio_Driver
{
    public partial class Form1 : Form
    {
        private NotifyIcon NotifyIcon;
        private ContextMenuStrip CMS;

        /// <summary>
        /// Initializes a NotifyIcon for system tray.
        /// </summary>
        private void InitializeNotifyIcon()
        {
            NotifyIcon = new NotifyIcon();
            NotifyIcon.Icon = Icon;
            NotifyIcon.Visible = true;
            NotifyIcon.DoubleClick += NotifyIcon_DoubleClick;
            NotifyIcon.MouseClick += NotifyIcon_MouseClick;
        }

        /// <summary>
        /// Initializes the ContextMenuStrip for the system tray icon.
        /// </summary>
        private void InitializeContextMenu()
        {
            CMS = new ContextMenuStrip();
            ToolStripMenuItem closeMenuItem = new ToolStripMenuItem("Close");
            CMS.Click += CMS_Click;
            CMS.Items.Add(closeMenuItem);
        }


        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            //Check if the user clicks the close button
            if (e.CloseReason == CloseReason.UserClosing)
            {
                //Cancel the event, hide the form and show a notification.
                e.Cancel = true;
                NotifyIcon.Visible = true;
                NotifyIcon.ShowBalloonTip(500, "Minimized to Tray", "The Audio Driver GUI has been minimized to the system tray.", ToolTipIcon.Info);
                Hide();
            }
        }

        private void Form1_Resize(object sender, EventArgs e)
        {
            //Handles minimization
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                NotifyIcon.Visible = true;
                NotifyIcon.ShowBalloonTip(500, "Minimized to Tray", "The Audio Driver GUI has been minimized to the system tray.", ToolTipIcon.Info);
            }
        }

        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            //If the form is not visible, show it and hide the tray icon.
            if (!Visible)
            {
                Show();
                WindowState = FormWindowState.Normal;
                NotifyIcon.Visible = false;                
            }
        }

        private void NotifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            //Show the context menu if the tray icon is right clicked
            if(e.Button == MouseButtons.Right)
            {
                CMS.Show(Cursor.Position);
            }
        }

        private void CMS_Click(object sender, EventArgs e)
        {
            //Closes the application on "Close" Option.
            Application.Exit();
        }
    }
}
