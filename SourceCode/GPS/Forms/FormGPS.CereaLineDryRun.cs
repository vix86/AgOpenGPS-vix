using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private CereaLineRegulator cereaLineDryRunRegulator;
        private Timer cereaLineDryRunTimer;

        public double CereaLineDryRunOutput { get; private set; }
        public double CereaLineDryRunXteMeters { get; private set; }
        public double CereaLineDryRunHeadingErrorDegrees { get; private set; }

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

    internal static class CereaLineDryRunBootstrap
    {
        private static bool hooked;
        private static FormGPS form;

        [ModuleInitializer]
        internal static void Init()
        {
            Application.Idle += Application_Idle;
        }

        private static void Application_Idle(object sender, EventArgs e)
        {
            if (hooked)
            {
                return;
            }

            form = Application.OpenForms["FormGPS"] as FormGPS;
            if (form == null)
            {
                return;
            }

            hooked = true;
            form.StartCereaLineDryRun();
            form.FormClosed += Form_FormClosed;
        }

        private static void Form_FormClosed(object sender, FormClosedEventArgs e)
        {
            Application.Idle -= Application_Idle;
            form = null;
        }
    }
}
