        private void ShowWindow()
        {
            Trace.WriteLine("[MainForm] ShowWindow called");
            // Use BeginInvoke to defer to UI thread queue after current event completes
            this.BeginInvoke(new Action(() => {
                Trace.WriteLine("[MainForm] BeginInvoke: Setting WindowState to Normal");
                this.WindowState = FormWindowState.Normal;
                this.Show();
                this.Activate();
                this.Focus();
                Trace.WriteLine("[MainForm] ShowWindow complete");
            }));
        }
