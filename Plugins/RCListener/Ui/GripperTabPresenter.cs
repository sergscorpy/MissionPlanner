using System;
using MissionPlanner;
using RCListener.Gripper;
using RCListener.Logging;

namespace RCListener.Ui
{
    public sealed class GripperTabPresenter : IDisposable
    {
        private readonly ILogger log;
        private readonly GripperControlService gripper;
        private GripperTabForm form;
        private bool disposed;

        public GripperTabPresenter(ILogger log, GripperControlService gripper)
        {
            this.log = log;
            this.gripper = gripper;
        }

        public void Initialize()
        {
            if (gripper == null)
                return;

            gripper.Updated += HandleUpdated;
            gripper.CommandTriggered += HandleCommandTriggered;
            HandleUpdated();
        }

        public void Dispose()
        {
            disposed = true;

            if (gripper != null)
            {
                gripper.Updated -= HandleUpdated;
                gripper.CommandTriggered -= HandleCommandTriggered;
            }

            ExecuteOnUi(() =>
            {
                if (form == null)
                    return;

                try
                {
                    form.Close();
                }
                catch (Exception ex)
                {
                    log?.Log($"[UI] GripperTab close error: {ex.Message}");
                }

                form = null;
            });
        }

        private void HandleUpdated()
        {
            ExecuteOnUi(() =>
            {
                if (disposed || gripper == null)
                    return;

                if (gripper.IsDeviceAvailable)
                {
                    EnsureForm();
                    form.UpdateFromService();
                    if (!form.Visible)
                        form.Show();
                    form.TopMost = true;
                }
                else
                {
                    if (form != null && !form.IsDisposed)
                        form.Hide();
                }
            });
        }

        private void HandleCommandTriggered(int servo, bool isLocked)
        {
            ExecuteOnUi(() =>
            {
                if (disposed)
                    return;

                if (form == null || form.IsDisposed)
                    return;

                form.StartAnimation(servo, isLocked);
            });
        }

        private void EnsureForm()
        {
            if (form != null && !form.IsDisposed)
                return;

            form = new GripperTabForm(log, gripper);
        }

        private void ExecuteOnUi(Action action)
        {
            var main = MainV2.instance;
            if (main != null && main.InvokeRequired)
                main.BeginInvoke(action);
            else
                action();
        }
    }
}