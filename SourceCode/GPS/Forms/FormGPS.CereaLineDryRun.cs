using System;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private static bool cereaLineDryRunHooked;
        private static FormGPS cereaLineDryRunForm;

        private CereaLineRegulator cereaLineDryRunRegulator;
        private Timer cereaLineDryRunTimer;

        public double CereaLineDryRunOutput { get; private set; }
        public double CereaLineDryRunXteMeters { get; private set; }
        public double CereaLineDryRunHeadingErrorDegrees { get; private set; }

        static FormGPS()
        {
            Application.Idle += CereaLineDryRunApplicationIdle;
        }

        private static void CereaLineDryRunApplicationIdle(object sender, EventArgs e)
        {
            if (cereaLineDryRunHooked)
            {
                return;
            }

            cereaLineDryRunForm = Application.OpenForms["FormGPS"] as FormGPS;
            if (cereaLineDryRunForm == null)
            {
                return;
            }

            cereaLineDryRunHooked = true;
            cereaLineDryRunForm.StartCereaLineDryRun();
            cereaLineDryRunForm.FormClosed += CereaLineDryRunFormClosed;
        }

        private static void CereaLineDryRunFormClosed(object sender, FormClosedEventArgs e)
        {
            Application.Idle -= CereaLineDryRunApplicationIdle;
            cereaLineDryRunForm = null;
        }

        private void StartCereaLineDryRun()
        {
            if (cereaLineDryRunTimer != null)
            {
                return;
            }

            cereaLineDryRunRegulator = new CereaLineRegulator
            {
                Enabled = false
            };

            cereaLineDryRunTimer = new Timer
            {
                Interval = 50
            };

            cereaLineDryRunTimer.Tick += CereaLineDryRunTimer_Tick;
            cereaLineDryRunTimer.Start();
        }

        private void CereaLineDryRunTimer_Tick(object sender, EventArgs e)
        {
            if (cereaLineDryRunRegulator == null || vehicle == null || pn == null)
            {
                return;
            }

            CereaLineDryRunXteMeters = vehicle.modeActualXTE;
            CereaLineDryRunHeadingErrorDegrees = vehicle.modeActualHeadingError;

            CereaLineDryRunOutput = cereaLineDryRunRegulator.Update(
                CereaLineDryRunXteMeters,
                CereaLineDryRunHeadingErrorDegrees,
                isBtnAutoSteerOn,
                pn.vtgSpeed,
                vehicle.minSteerSpeed);
        }
    }
}
