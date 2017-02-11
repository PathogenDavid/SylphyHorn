using MetroTrilithon.Lifetime;
using SylphyHorn.Properties;
using SylphyHorn.Serialization;
using SylphyHorn.UI;
using SylphyHorn.UI.Bindings;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WindowsDesktop;

namespace SylphyHorn.Services
{
	public class NotificationService : IDisposable
	{
		public static NotificationService Instance { get; } = new NotificationService();

		private readonly SerialDisposable _notificationWindow = new SerialDisposable();

		private const string desktopNamesFile = "DesktopNames.txt";
		private string[] desktopNames;
		private readonly object desktopNamesLock = new Object();
		private FileSystemWatcher desktopNamesFileWatcher = null;

		private NotificationService()
		{
			desktopNamesFileWatcher = new FileSystemWatcher(Environment.CurrentDirectory, desktopNamesFile);
			desktopNamesFileWatcher.Created += DesktopNamesFileChanged;
			desktopNamesFileWatcher.Changed += DesktopNamesFileChanged;
			desktopNamesFileWatcher.Deleted += DesktopNamesFileChanged;
			desktopNamesFileWatcher.Renamed += DesktopNamesFileChanged;
			desktopNamesFileWatcher.EnableRaisingEvents = true;

			LoadDesktopNames();

			VirtualDesktop.CurrentChanged += this.VirtualDesktopOnCurrentChanged;
			VirtualDesktopService.WindowPinned += this.VirtualDesktopServiceOnWindowPinned;
		}

		private void VirtualDesktopOnCurrentChanged(object sender, VirtualDesktopChangedEventArgs e)
		{
			if (!Settings.General.NotificationWhenSwitchedDesktop) return;

			VisualHelper.InvokeOnUIDispatcher(() =>
			{
				var desktops = VirtualDesktop.GetDesktops();
				var newIndex = Array.IndexOf(desktops, e.NewDesktop) + 1;

				this._notificationWindow.Disposable = ShowDesktopWindow(newIndex);
			});
		}

		private void VirtualDesktopServiceOnWindowPinned(object sender, WindowPinnedEventArgs e)
		{
			VisualHelper.InvokeOnUIDispatcher(() =>
			{
				this._notificationWindow.Disposable = ShowPinWindow(e.Target, e.PinOperation);
			});
		}

		private void LoadDesktopNames()
		{
			lock (desktopNamesLock)
			{
				if (!File.Exists(desktopNamesFile))
				{
					desktopNames = null;
					return;
				}

				desktopNames = File.ReadAllLines(desktopNamesFile);
			}
		}

		private void DesktopNamesFileChanged(object sender, FileSystemEventArgs e)
		{ LoadDesktopNames(); }

		private string GetDesktopName(int index)
		{
			string ret = null;
			int zeroIndex = index - 1;

			lock (desktopNamesLock)
			{
				if (desktopNames != null && zeroIndex >= 0 && zeroIndex < desktopNames.Length)
				{ ret = desktopNames[zeroIndex]; }
			}

			if (String.IsNullOrWhiteSpace(ret))
			{ ret = $"Desktop {index}"; }

			return ret;
		}

		private IDisposable ShowDesktopWindow(int index)
		{
			var vmodel = new NotificationWindowViewModel
			{
				Title = ProductInfo.Title,
				Header = "Virtual Desktop Switched",
				Body = GetDesktopName(index),
			};
			var source = new CancellationTokenSource();
			var window = new NotificationWindow()
			{
				DataContext = vmodel,
			};
			window.Show();

			Task.Delay(TimeSpan.FromMilliseconds(Settings.General.NotificationDuration), source.Token)
				.ContinueWith(_ => window.Close(), TaskScheduler.FromCurrentSynchronizationContext());

			return Disposable.Create(() => source.Cancel());
		}

		private static IDisposable ShowPinWindow(IntPtr hWnd, PinOperations operation)
		{
			var vmodel = new NotificationWindowViewModel
			{
				Title = ProductInfo.Title,
				Header = ProductInfo.Title,
				Body = $"{(operation.HasFlag(PinOperations.Pin) ? "Pinned" : "Unpinned")} this {(operation.HasFlag(PinOperations.Window) ? "window" : "application")}",
			};
			var source = new CancellationTokenSource();
			var window = new PinNotificationWindow(hWnd)
			{
				DataContext = vmodel,
			};
			window.Show();

			Task.Delay(TimeSpan.FromMilliseconds(Settings.General.NotificationDuration), source.Token)
				.ContinueWith(_ => window.Close(), TaskScheduler.FromCurrentSynchronizationContext());

			return Disposable.Create(() => source.Cancel());
		}

		public void Dispose()
		{
			desktopNamesFileWatcher.Dispose();

			VirtualDesktop.CurrentChanged -= this.VirtualDesktopOnCurrentChanged;
			VirtualDesktopService.WindowPinned -= this.VirtualDesktopServiceOnWindowPinned;

			this._notificationWindow.Dispose();
		}
	}
}
