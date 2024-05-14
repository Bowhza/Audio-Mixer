namespace Audio_Driver
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.Sessions_List = new System.Windows.Forms.ListBox();
            this.label1 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // Sessions_List
            // 
            this.Sessions_List.BackColor = System.Drawing.Color.WhiteSmoke;
            this.Sessions_List.Font = new System.Drawing.Font("JetBrains Mono", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Sessions_List.ForeColor = System.Drawing.SystemColors.WindowText;
            this.Sessions_List.FormattingEnabled = true;
            this.Sessions_List.ItemHeight = 21;
            this.Sessions_List.Location = new System.Drawing.Point(12, 33);
            this.Sessions_List.Name = "Sessions_List";
            this.Sessions_List.Size = new System.Drawing.Size(358, 361);
            this.Sessions_List.TabIndex = 0;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("JetBrains Mono", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.Location = new System.Drawing.Point(12, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(160, 21);
            this.label1.TabIndex = 1;
            this.label1.Text = "Active Sessions";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(734, 411);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.Sessions_List);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "Form1";
            this.Text = "Audio Driver";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ListBox Sessions_List;
        private System.Windows.Forms.Label label1;
    }
}

