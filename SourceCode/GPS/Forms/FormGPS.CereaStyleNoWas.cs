using System;
using System.Drawing;
using System.Windows.Forms;
using AgOpenGPS.Hardware.CereaStyle;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private const int CereaPanelExpandedWidth = 760;
        private const int CereaPanelExpandedHeight = 92;
        private const int CereaPanelCollapsedWidth = 92;
        private const int CereaPanelCollapsedHeight = 38;

        private Panel cereaNoWasPanel;
        private Button cereaToggleButton;
        private Button cereaConnectButton;
        private Button cereaArmButton;
        private Button cereaStartStopButton;
        private Button cereaDisarmButton;
        private Button cereaZeroButton;
        private Button cereaDiagButton;
        private Label cereaStatusLabel;
        private Timer cereaNoWasTimer;
        private CereaStyleNoWasController cereaNoWasController;
        private PhidgetsCereaMotor cereaPhidgetsMotor;
        private TinkerforgeCereaImu cereaImu;
        private CereaStyleNoWasOptions cereaNoWasOptions;
        private bool cereaNoWasInitChecked;
        private bool cereaNoWasEnabled;
        private bool cereaNoWasDisposed;
        private bool cereaNoWasPanelCollapsed;
        private bool cereaNoWasAutoConnected;
        private bool cereaNoWasLastAogAutoSteerOn;

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
            cereaImu = new TinkerforgeCereaImu(cereaNoWasOptions.ImuSettings);

            cereaNoWasPanel = new Panel
            {
                Name = "cereaNoWasPanel",
                Left = 82,
                Top = 78,
                Width = CereaPanelExpandedWidth,
                Height = CereaPanelExpandedHeight,
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };

            cereaToggleButton = MakeCereaButton("CEREA -", 6, 6, 82, CereaToggleButton_Click);
            cereaConnectButton = MakeCereaButton("CONNECT", 94, 6, 78, CereaConnectButton_Click);
            cereaArmButton = MakeCereaButton("ARM", 178, 6, 58, CereaArmButton_Click);
            cereaStartStopButton = MakeCereaButton("START", 242, 6, 70, CereaStartStopButton_Click);
            cereaDisarmButton = MakeCereaButton("DISARM", 318, 6, 76, CereaDisarmButton_Click);
            cereaZeroButton = MakeCereaButton("ZERO ENC", 400, 6, 80, CereaZeroButton_Click);
            cereaDiagButton = MakeCereaButton("DIAG", 486, 6, 62, CereaDiagButton_Click);

            cereaStatusLabel = new Label
            {
                Left = 6,
                Top = 40,
                Width = 744,
                Height = 44,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Text = "Cerea No-WAS: " + cereaNoWasOptions.StatusMessage
            };

            cereaNoWasPanel.Controls.Add(cereaToggleButton);
            cereaNoWasPanel.Controls.Add(cereaConnectButton);
            cereaNoWasPanel.Controls.Add(cereaArmButton);
            cereaNoWasPanel.Controls.Add(cereaStartStopButton);
            cereaNoWasPanel.Controls.Add(cereaDisarmButton);
            cereaNoWasPanel.Controls.Add(cereaZeroButton);
            cereaNoWasPanel.Controls.Add(cereaDiagButton);
            cereaNoWasPanel.Controls.Add(cereaStatusLabel);
            Controls.Add(cereaNoWasPanel);
            cereaNoWasPanel.BringToFront();

            if (cereaNoWasOptions.PanelMinimized)
            {
                SetCereaPanelCollapsed(true);
            }

            cereaNoWasTimer = new Timer { Interval = 50 };
            cereaNoWasTimer.Tick += CereaNoWasTimer_Tick;
            cereaNoWasTimer.Start();

            if (cereaNoWasOptions.AutoConnect)
            {
                AutoConnectCereaNoWas();
            }
            if (cereaNoWasOptions.AutoArm && cereaNoWasController != null)
            {
                cereaNoWasController.Arm();
                UpdateCereaNoWasStatus("AUTO ARM. " + BuildCereaDiagText(""));
            }
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

        private void CereaToggleButton_Click(object sender, EventArgs e)
        {
            SetCereaPanelCollapsed(!cereaNoWasPanelCollapsed);
        }

        private void SetCereaPanelCollapsed(bool collapsed)
        {
            cereaNoWasPanelCollapsed = collapsed;
            if (cereaNoWasPanel == null) return;

            cereaNoWasPanel.SuspendLayout();
            cereaNoWasPanel.Width = collapsed ? CereaPanelCollapsedWidth : CereaPanelExpandedWidth;
            cereaNoWasPanel.Height = collapsed ? CereaPanelCollapsedHeight : CereaPanelExpandedHeight;
            if (cereaToggleButton != null) cereaToggleButton.Text = collapsed ? "CEREA" : "CEREA -";
            if (cereaStatusLabel != null) cereaStatusLabel.Visible = !collapsed;
            if (cereaConnectButton != null) cereaConnectButton.Visible = !collapsed;
            if (cereaArmButton != null) cereaArmButton.Visible = !collapsed;
            if (cereaStartStopButton != null) cereaStartStopButton.Visible = !collapsed;
            if (cereaDisarmButton != null) cereaDisarmButton.Visible = !collapsed;
            if (cereaZeroButton != null) cereaZeroButton.Visible = !collapsed;
            if (cereaDiagButton != null) cereaDiagButton.Visible = !collapsed;
            cereaNoWasPanel.ResumeLayout();
            cereaNoWasPanel.BringToFront();
        }

        private void AutoConnectCereaNoWas()
        {
            if (cereaNoWasAutoConnected || cereaPhidgetsMotor == null) return;
            cereaNoWasAutoConnected = true;
            cereaPhidgetsMotor.Connect();
            if (cereaNoWasOptions != null && cereaNoWasOptions.ImuSettings.Enabled && cereaImu != null)
            {
                cereaImu.Connect();
            }
            UpdateCereaNoWasStatus("AUTO CONNECT. " + BuildCereaDiagText(""));
        }

        private void CereaConnectButton_Click(object sender, EventArgs e)
        {
            if (!cereaNoWasEnabled || cereaPhidgetsMotor == null) return;
            cereaNoWasAutoConnected = false;
            AutoConnectCereaNoWas();
            UpdateCereaNoWasStatus(BuildCereaDiagText("CONNECT"));
        }

        private void CereaArmButton_Click(object sender, EventArgs e)
        {
            if (!cereaNoWasEnabled || cereaNoWasController == null || cereaPhidgetsMotor == null) return;
            cereaNoWasController.Arm();
            cereaPhidgetsMotor.Stop();
            UpdateCereaNoWasStatus("ARMED. AOG AutoSteer controls motor output. " + BuildCereaDiagText(""));
        }

        private void CereaStartStopButton_Click(object sender, EventArgs e)
        {
            if (!cereaNoWasEnabled || cereaNoWasController == null || cereaPhidgetsMotor == null) return;
            if (cereaNoWasController.IsRunning)
            {
                cereaNoWasController.Stop();
                cereaPhidgetsMotor.Stop();
                SetCereaStartButton(false);
                UpdateCereaNoWasStatus("Stopped. " + BuildCereaDiagText(""));
                return;
            }

            StartCereaNoWasIfSafe("START");
        }

        private void StartCereaNoWasIfSafe(string prefix)
        {
            if (cereaNoWasController == null || cereaPhidgetsMotor == null) return;
            if (!cereaPhidgetsMotor.MotorConnected || !cereaPhidgetsMotor.EncoderConnected)
            {
                cereaPhidgetsMotor.Stop();
                SetCereaStartButton(false);
                UpdateCereaNoWasStatus(prefix + ": blocked, motor/encoder not connected. " + BuildCereaDiagText(""));
                return;
            }

            if (!cereaNoWasController.IsArmed)
            {
                cereaNoWasController.Arm();
            }

            cereaNoWasController.Start();
            SetCereaStartButton(true);
            UpdateCereaNoWasStatus(prefix + ": running. " + BuildCereaDiagText(""));
        }

        private void CereaDisarmButton_Click(object sender, EventArgs e)
        {
            HardStopCereaNoWas("DISARMED. Motor stopped.");
        }

        private void CereaZeroButton_Click(object sender, EventArgs e)
        {
            if (!cereaNoWasEnabled || cereaPhidgetsMotor == null) return;
            cereaPhidgetsMotor.ResetEncoderZero();
            UpdateCereaNoWasStatus("Encoder zero set. " + BuildCereaDiagText(""));
        }

        private void CereaDiagButton_Click(object sender, EventArgs e)
        {
            if (!cereaNoWasEnabled) return;
            UpdateCereaNoWasStatus(BuildCereaDiagText("DIAG"));
        }

        private string BuildCereaDiagText(string prefix)
        {
            if (cereaPhidgetsMotor != null)
            {
                cereaPhidgetsMotor.RefreshEncoderPosition();
            }
            if (cereaImu != null && cereaNoWasOptions != null && cereaNoWasOptions.ImuSettings.Enabled)
            {
                cereaImu.Refresh();
            }

            var p = string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix + ": ";
            var cfg = cereaNoWasOptions == null ? "config=-" : "config=" + cereaNoWasOptions.Enabled;
            var motor = cereaPhidgetsMotor == null ? "motor=- encoder=- enc=0 vel=0" :
                "motor=" + cereaPhidgetsMotor.MotorConnected +
                " encoder=" + cereaPhidgetsMotor.EncoderConnected +
                " enc=" + cereaPhidgetsMotor.EncoderCounts +
                " vel=" + cereaPhidgetsMotor.LastTargetVelocity.ToString("0.000");
            var imu = cereaImu == null || cereaNoWasOptions == null || !cereaNoWasOptions.ImuSettings.Enabled ? " imu=off" : " " + cereaImu.GetStatusText();
            var fault = cereaNoWasController == null ? string.Empty : " fault=" + cereaNoWasController.LastFault;
            var err = cereaPhidgetsMotor == null || string.IsNullOrWhiteSpace(cereaPhidgetsMotor.LastError) ? string.Empty : " error=" + cereaPhidgetsMotor.LastError;
            return p + cfg + " " + motor + imu + fault + err;
        }

        private void CereaNoWasTimer_Tick(object sender, EventArgs e)
        {
            if (!cereaNoWasEnabled || cereaNoWasController == null || cereaPhidgetsMotor == null) return;
            cereaPhidgetsMotor.RefreshEncoderPosition();

            if (cereaNoWasOptions != null && cereaNoWasOptions.AutoConnect && !cereaPhidgetsMotor.MotorConnected && !cereaNoWasAutoConnected)
            {
                AutoConnectCereaNoWas();
            }

            if (cereaImu != null && cereaNoWasOptions != null && cereaNoWasOptions.ImuSettings.Enabled)
            {
                cereaImu.Refresh();
                if (cereaNoWasOptions.ImuSettings.FeedAogAhrs && cereaImu.Connected)
                {
                    ahrs.imuHeading = cereaImu.HeadingDegrees;
                    ahrs.imuRoll = cereaImu.RollDegrees;
                }
            }

            if (!isBtnAutoSteerOn)
            {
                cereaNoWasLastAogAutoSteerOn = false;
                cereaPhidgetsMotor.Stop();
                if (cereaNoWasController.IsRunning)
                {
                    cereaNoWasController.Stop();
                    SetCereaStartButton(false);
                    UpdateCereaNoWasStatus("AOG AutoSteer OFF. Motor=0. " + BuildCereaDiagText(""));
                }
                return;
            }

            if (cereaNoWasOptions != null && cereaNoWasOptions.AutoStartWithAogAutoSteer && !cereaNoWasLastAogAutoSteerOn)
            {
                cereaNoWasLastAogAutoSteerOn = true;
                if (cereaNoWasOptions.AutoArm && !cereaNoWasController.IsArmed)
                {
                    cereaNoWasController.Arm();
                }
                StartCereaNoWasIfSafe("AUTO START");
            }

            if (!cereaNoWasController.IsRunning)
            {
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

            if (output.SafetyState == CereaStyleSafetyState.Fault)
            {
                SetCereaStartButton(false);
            }

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

        private void SetCereaStartButton(bool running)
        {
            if (cereaStartStopButton != null)
            {
                cereaStartStopButton.Text = running ? "STOP" : "START";
            }
        }

        private void HardStopCereaNoWas(string message)
        {
            if (!cereaNoWasEnabled) return;
            try { cereaNoWasController?.Disarm(); } catch { }
            try { cereaPhidgetsMotor?.Stop(); } catch { }
            SetCereaStartButton(false);
            cereaNoWasLastAogAutoSteerOn = false;
            UpdateCereaNoWasStatus(message + " " + BuildCereaDiagText(""));
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
            if (!cereaNoWasEnabled || cereaNoWasDisposed) return;
            cereaNoWasDisposed = true;
            try { cereaNoWasTimer?.Stop(); } catch { }
            try { cereaNoWasTimer?.Dispose(); } catch { }
            try { cereaNoWasController?.Disarm(); } catch { }
            try { cereaPhidgetsMotor?.Stop(); } catch { }
            try { cereaPhidgetsMotor?.Dispose(); } catch { }
            try { cereaImu?.Dispose(); } catch { }
            SetCereaStartButton(false);
        }
    }
}
