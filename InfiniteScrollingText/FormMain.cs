using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Reflection;

namespace InfiniteScrollingText
{
    public partial class FormMain : Form
    {
        private Bogus.DataSets.Lorem lorem;

        public FormMain()
        {
            InitializeComponent();

            this.lorem = new Bogus.DataSets.Lorem("en");

            this.boxConsole.TopRowChanged += (s, e) => UpdateText();
            this.boxConsole.BottomRowChanged += (s, e) => UpdateText();

            this.boxConsole.DrawMode = TextDrawMode.ExtTextOut;
            this.boxConsole.BufferSize = 200;
            this.boxConsole.AutoScrollLastRow = true;
            this.timerAddRows.Interval = 1;
            //this.timerAddRows.Enabled = false;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (!this.timerAddRows.Enabled)
            {
                boxConsole.BeginUpdate();

                for (int i = 0; i < 20; i++)
                {
                    timerAddRows_Tick(null, null);
                }

                boxConsole.EndUpdate();
            }
        }

        private void timerAddRows_Tick(object sender, EventArgs e)
        {
            boxConsole.BeginUpdate();
            for (int i = 0, c = 10 + this.lorem.Random.Number(40); i < c; i++)
            {
                boxConsole.AppendText(this.lorem.Sentence(wordCount: 10 + this.lorem.Random.Number(40)));
            }
            boxConsole.EndUpdate();

            UpdateText();
        }

        private void UpdateText()
        {
            this.Text = $"TopMost: {this.boxConsole.TopRow}, BottomMost: {this.boxConsole.BottomRow}, RowCount: {this.boxConsole.RowCount}, VirtualRowCount: {this.boxConsole.VirtualRowCount}";
        }
    }
}
