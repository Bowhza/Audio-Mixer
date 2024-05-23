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
        private ContextMenuStrip ContextMStrip;

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
            ContextMStrip = new ContextMenuStrip();
            ToolStripMenuItem closeMenuItem = new ToolStripMenuItem("Close");
            ContextMStrip.Click += ContextMStrip_Click;
            ContextMStrip.Items.Add(closeMenuItem);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (WindowState == FormWindowState.Minimized)
            {
                //Show balloon tip when minimized to system tray
                NotifyIcon.Visible = true;
                NotifyIcon.ShowBalloonTip(500, "Minimized to Tray", "The Audio Driver GUI has been minimized to the system tray.", ToolTipIcon.Info);
                //Hide the form
                Hide();
            }
        }

        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            //If the form is not visible, show it and hide the tray icon.
            if (!Visible)
            {
                Show();
                WindowState = FormWindowState.Normal;
                NotifyIcon.Visible = true;
            }
        }

        private void NotifyIcon_MouseClick(object sender, MouseEventArgs e)
        {
            //Show the context menu if the tray icon is right clicked
            if (e.Button == MouseButtons.Right)
            {
                ContextMStrip.Show(Cursor.Position);
            }
        }

        private void ContextMStrip_Click(object sender, EventArgs e)
        {
            //Closes the application on "Close" Option.
            NotifyIcon.Visible = false;
            Application.Exit();
        }
    }
}
