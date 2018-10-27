namespace InfiniteScrollingText
{
    partial class FormMain
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
            this.components = new System.ComponentModel.Container();
            this.timerAddRows = new System.Windows.Forms.Timer(this.components);
            this.boxConsole = new InfiniteScrollingText.InfiniteScrollableControl();
            this.SuspendLayout();
            // 
            // timerAddRows
            // 
            this.timerAddRows.Enabled = true;
            this.timerAddRows.Interval = 250;
            this.timerAddRows.Tick += new System.EventHandler(this.timerAddRows_Tick);
            // 
            // boxConsole
            // 
            this.boxConsole.BackColor = System.Drawing.Color.Black;
            this.boxConsole.Dock = System.Windows.Forms.DockStyle.Fill;
            this.boxConsole.EvenRowBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.boxConsole.EvenRowForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(255)))), ((int)(((byte)(192)))));
            this.boxConsole.Font = new System.Drawing.Font("Consolas", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.boxConsole.ForeColor = System.Drawing.Color.White;
            this.boxConsole.Location = new System.Drawing.Point(0, 0);
            this.boxConsole.Name = "boxConsole";
            this.boxConsole.Size = new System.Drawing.Size(1072, 648);
            this.boxConsole.TabIndex = 0;
            // 
            // FormMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.Silver;
            this.ClientSize = new System.Drawing.Size(1072, 648);
            this.Controls.Add(this.boxConsole);
            this.Name = "FormMain";
            this.Text = "Test Form";
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.Timer timerAddRows;
        private InfiniteScrollableControl boxConsole;
    }
}

