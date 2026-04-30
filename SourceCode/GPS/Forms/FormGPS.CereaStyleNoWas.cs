using System;
using System.Drawing;
using System.Windows.Forms;
using AgOpenGPS.Hardware.CereaStyle;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private Panel cereaNoWasPanel;
        private Button cereaConnectButton;
        private Button cereaArmButton;
        private Button cereaStartStopButton;
        private Button cereaDisarmButton;
        private Button cereaZeroButton;
        private Label cereaStatusLabel;
        private Timer cereaNoWasTimer;
        private CereaStyleNoWasController cereaNoWasController;
        private PhidgetsCereaMotor cereaPhidgetsMotor;
        private CereaStyleNoWasOptions cereaNoWasOptions;
        private bool cereaNoWasInitChecked;
        private bool cereaNoWasEnabled;

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible || cereaNoWasInitChecked) return;

            cereaNoWasInitChecked = true;
            cereaNoWasOptions = CereaStyleNoWasOptions.Load();
            cereaNoWasEnabled = cereaNoWasOptions.Enabled;
            if (cereaNoWasEnabled)
            {
                InitializeCereaStyleNoWasMode();
            }
        }

        private void InitializeCereaStyleNoWasMode()
        {
            if (!cereaNoWasEnabled || cereaNoWasOptions == null) return;
            if (cereaNoWasPanel != null) return;

            FormClosed += delegate { SafeStopCereaStyleNoWasMode(); };

            cereaNoWasController = new CereaStyleNoWasController(cereaNoWasOptions.ControllerSettings);
            cereaPhidgetsMotor = new PhidgetsCereaMotor(cereaNoWasOptions.MotorSettings);

            cereaNoWasPanel = new Panel
            {
                Name = "cereaNoWasPanel",
                Left = 82,
                Top = 78,
                Width = 610,
                Height = 82,
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            cereaConnectButton = MakeCereaButton("CONNECT", 6, 6, 78, CereaConnectButton_Click);
            cereaArmButton = MakeCereaButton("ARM", 90, 6, 58, CereaArmButton_Click);
            cereaStartStopButton = MakeCereaButton("START", 154, 6, 70, CereaStartStopButton_Click);
            cereaDisarmButton = MakeCereaButton("DISARM", 230, 6, 76, CereaDisarmButton_Click);
            cereaZeroButton = MakeCereaButton("ZERO ENC", 312, 6, 80, CereaZeroButton_Click);

            cereaStatusLabel = new Label
            {
                Left = 6,
                Top = 40,
                Width = 590,
                Height = 34,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Text = "Cerea No-WAS: " + cereaNoWasOptions.StatusMessage
            };

            cereaNoWasPanel.Controls.Add(cereaConnectButton);
            cereaNoWasPanel.Controls.Add(cereaArmButton);
            cereaNoWasPanel.Controls.Add(cereaStartStopButton);
            cereaNoWasPanel.Controls.Add(cereaDisarmButton);
            cereaNoWasPanel.Controls.Add(cereaZeroButton);
            cereaNoWasPanel.Controls.Add(cereaStatusLabel);
            Controls.Add(cereaNoWasPanel);
            cereaNoWasPanel.BringToFront();

            cereaNoWasTimer = new Timer { Interval = 50 };
            cereaNoWasTimer.Tick += CereaNoWasTimer_Tick;
            cereaNoWasTimer.Start();
        }

        private static Button MakeCereaButton(string text, int left, int top, int width, EventHandler click)
        {
            var button = new Button
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = 30,
                BackColor = Color.Gainsboro,
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Standard
            };
            button.Click += click;
            return button;
        }

        private void CereaConnectButton_Click(object sender, EventArgs e)
        {
            if (!cereaNoWasEnabled || cereaPhidgetsMotor == null) return;
            cereaPhidgetsMotor.Connect();
            UpdateCereaNoWasStatus("Connect: motor=" + cereaPhidgetsMotor.MotorConnected + " encoder=" + cereaPhidgetsMotor.EncoderConnected + " enc=" + cereaPhidgetsMotor.EncoderCounts + " " + cereaPhidgetsMotor.LastError);
        }

        private void CereaArmButton_Click(object sender, EventArgs e)
        {
            if (!cereaNoWasEnabled || cereaNoWasController == null || cereaPhidgetsMotor == null) return;
            cereaNoWasController.Arm();
            cereaPhidgetsMotor.Stop();
            UpdateCereaNoWasStatus("ARMED. Press AOG AutoSteer + START to allow motor output.");
        }

        private void CereaStartStopButton_Click(object sender, EventArgs e)
        {
            if (!cereaNoWasEnabled || cereaNoWasController == null || cereaPhidgetsMotor == null) return;
            if (cereaNoWasController.IsRunning)
            {
                cereaNoWasController.Stop();
                cereaPhidgetsMotor.Stop();
                cereaStartStopButton.Text = "START";
                UpdateCereaNoWasStatus("Stopped.");
                return;
            }

            if (!cereaNoWasController.IsArmed)
            {
                cereaNoWasController.Arm();
            }

            cereaNoWasController.Start();
            cereaStartStopButton.Text = "STOP";
            UpdateCereaNoWasStatus("Started. Motor output still requires AOG AutoSteer ON and RTK FIX.");
        }

        private void CereaDisarmButton_Click(object sender, EventArgs e)
        {
            if (!cereaNoWasEnabled || cereaNoWasController == null || cereaPhidgetsMotor == null) return;
            cereaNoWasController.Disarm();
            cereaPhidgetsMotor.Stop();
            cereaStartStopButton.Text = "START";
            UpdateCereaNoWasStatus("DISARMED. Motor stopped.");
        }

        private void CereaZeroButton_Click(object sender, EventArgs e)
        {
            if (!cereaNoWasEnabled || cereaPhidgetsMotor == null) return;
            cereaPhidgetsMotor.ResetEncoderZero();
            UpdateCereaNoWasStatus("Encoder zero set. Counts=" + cereaPhidgetsMotor.EncoderCounts);
        }

        private void CereaNoWasTimer_Tick(object sender, EventArgs e)
        {
            if (!cereaNoWasEnabled || cereaNoWasController == null || cereaPhidgetsMotor == null) return;
            cereaPhidgetsMotor.RefreshEncoderPosition();

            if (!isBtnAutoSteerOn)
            {
                cereaPhidgetsMotor.Stop();
                if (cereaNoWasController.IsRunning)
                {
                    UpdateCereaNoWasStatus("Waiting: AOG AutoSteer button is OFF. Motor=0. enc=" + cereaPhidgetsMotor.EncoderCounts);
                }
                return;
            }

            var input = new CereaStyleNoWasInput
            {
                GpsValid = pn != null && pn.fixQuality > 0,
                RtkFixed = pn != null && pn.fixQuality == 4,
                AbLineValid = ABLine != null && ABLine.isABValid,
                GpsAgeMilliseconds = pn == null ? 9999 : (int)Math.Max(0.0, pn.age * 1000.0),
                SpeedKph = avgSpeed,
                CrossTrackErrorMeters = ABLine == null ? 0.0 : ABLine.distanceFromCurrentLinePivot,
                HeadingErrorDegrees = vehicle == null ? 0.0 : vehicle.modeActualHeadingError,
                EncoderCounts = cereaPhidgetsMotor.EncoderCounts,
                NowUtc = DateTime.UtcNow
            };

            var output = cereaNoWasController.Update(input);
            cereaPhidgetsMotor.ApplyCommand(output.MotorCommand);

            if (cereaNoWasController.IsRunning || output.SafetyState == CereaStyleSafetyState.Fault)
            {
                UpdateCereaNoWasStatus(
                    "state=" + output.SafetyState +
                    " fix=" + (pn == null ? 0 : pn.fixQuality) +
                    " spd=" + avgSpeed.ToString("0.0") +
                    " xte=" + input.CrossTrackErrorMeters.ToString("0.00") +
                    " hdgErr=" + input.HeadingErrorDegrees.ToString("0.0") +
                    " enc=" + output.EncoderCounts +
                    " cmd=" + output.MotorCommand.ToString("0.000") +
                    " vel=" + cereaPhidgetsMotor.LastTargetVelocity.ToString("0.000") +
                    (string.IsNullOrWhiteSpace(output.Fault) ? string.Empty : " fault=" + output.Fault));
            }
        }

        private void UpdateCereaNoWasStatus(string text)
        {
            if (cereaStatusLabel != null)
            {
                cereaStatusLabel.Text = "Cerea No-WAS: " + text;
            }
        }

        private void SafeStopCereaStyleNoWasMode()
        {
            if (!cereaNoWasEnabled) return;
            try { cereaNoWasTimer?.Stop(); } catch { }
            try { cereaNoWasController?.Disarm(); } catch { }
            try { cereaPhidgetsMotor?.Stop(); } catch { }
            try { cereaPhidgetsMotor?.Dispose(); } catch { }
        }
    }
}
