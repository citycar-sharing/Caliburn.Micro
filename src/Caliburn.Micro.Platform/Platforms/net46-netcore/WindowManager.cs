using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace Caliburn.Micro
{
    /// <summary>
    /// A service that manages windows.
    /// </summary>
    public interface IWindowManager
    {
        /// <summary>
        /// Shows a modal dialog for the specified model.
        /// </summary>
        /// <param name="rootModel">The root model.</param>
        /// <param name="context">The context.</param>
        /// <param name="settings">The optional dialog settings.</param>
        /// <returns>The dialog result.</returns>
        Task<bool?> ShowDialogAsync(object rootModel, object context = null, IDictionary<string, object> settings = null, Dispatcher p_disp = null,
            bool? p_useViewDispatcher = null);

        /// <summary>
        /// Shows a non-modal window for the specified model.
        /// </summary>
        /// <param name="rootModel">The root model.</param>
        /// <param name="context">The context.</param>
        /// <param name="settings">The optional window settings.</param>
        Task ShowWindowAsync(object rootModel, object context = null, IDictionary<string, object> settings = null);

        /// <summary>
        /// Shows a popup at the current mouse position.
        /// </summary>
        /// <param name="rootModel">The root model.</param>
        /// <param name="context">The view context.</param>
        /// <param name="settings">The optional popup settings.</param>
        Task ShowPopupAsync(object rootModel, object context = null, IDictionary<string, object> settings = null);
    }

    /// <summary>
    /// A service that manages windows.
    /// </summary>
    public class WindowManager : IWindowManager
    {
        /// <summary>
        /// Shows a modal dialog for the specified model.
        /// </summary>
        /// <param name="rootModel">The root model.</param>
        /// <param name="context">The context.</param>
        /// <param name="settings">The dialog popup settings.</param>
        /// <returns>The dialog result.</returns>
        public virtual async Task<bool?> ShowDialogAsync(object rootModel, object context = null, IDictionary<string, object> settings = null, Dispatcher p_disp = null,
            bool? p_useViewDispatcher = null)
        {
            var window = await CreateWindowAsync(rootModel, true, context, settings, p_disp, p_useViewDispatcher);

            return window.ShowDialog();
        }

        /// <summary>
        /// Shows a window for the specified model.
        /// </summary>
        /// <param name="rootModel">The root model.</param>
        /// <param name="context">The context.</param>
        /// <param name="settings">The optional window settings.</param>
        public virtual async Task ShowWindowAsync(object rootModel, object context = null, IDictionary<string, object> settings = null)
        {
            NavigationWindow navWindow = null;

            var application = Application.Current;
            if (application != null && application.MainWindow != null)
            {
                navWindow = application.MainWindow as NavigationWindow;
            }

            if (navWindow != null)
            {
                var window = await CreatePageAsync(rootModel, context, settings);
                navWindow.Navigate(window);
            }
            else
            {
                var window = await CreateWindowAsync(rootModel, false, context, settings);

                window.Show();
            }
        }

        /// <summary>
        /// Shows a popup at the current mouse position.
        /// </summary>
        /// <param name="rootModel">The root model.</param>
        /// <param name="context">The view context.</param>
        /// <param name="settings">The optional popup settings.</param>
        public virtual async Task ShowPopupAsync(object rootModel, object context = null, IDictionary<string, object> settings = null)
        {
            var popup = CreatePopup(rootModel, settings);
            var view = ViewLocator.LocateForModel(rootModel, popup, context);

            popup.Child = view;
            popup.SetValue(View.IsGeneratedProperty, true);

            ViewModelBinder.Bind(rootModel, popup, null);
            Action.SetTargetWithoutContext(view, rootModel);

            if (rootModel is IActivate activator)
            {
                await activator.ActivateAsync();
            }

            if (rootModel is IDeactivate deactivator)
            {
                popup.Closed += async (s, e) => await deactivator.DeactivateAsync(true);
            }

            popup.IsOpen = true;
            popup.CaptureMouse();
        }

        /// <summary>
        /// Creates a popup for hosting a popup window.
        /// </summary>
        /// <param name="rootModel">The model.</param>
        /// <param name="settings">The optional popup settings.</param>
        /// <returns>The popup.</returns>
        protected virtual Popup CreatePopup(object rootModel, IDictionary<string, object> settings)
        {
            var popup = new Popup();

            if (ApplySettings(popup, settings))
            {
                if (!settings.ContainsKey("PlacementTarget") && !settings.ContainsKey("Placement"))
                {
                    popup.Placement = PlacementMode.MousePoint;
                }

                if (!settings.ContainsKey("AllowsTransparency"))
                {
                    popup.AllowsTransparency = true;
                }
            }
            else
            {
                popup.AllowsTransparency = true;
                popup.Placement = PlacementMode.MousePoint;
            }

            return popup;
        }

        /// <summary>
        /// Creates a window.
        /// </summary>
        /// <param name="rootModel">The view model.</param>
        /// <param name="isDialog">Whethor or not the window is being shown as a dialog.</param>
        /// <param name="context">The view context.</param>
        /// <param name="settings">The optional popup settings.</param>
        /// <returns>The window.</returns>
        protected virtual async Task<Window> CreateWindowAsync(
            object rootModel, 
            bool isDialog, 
            object context, 
            IDictionary<string, object> settings, 
            Dispatcher p_disp = null,
            bool? p_useViewDispatcher = null)
        {
            if (p_useViewDispatcher != true)
            {
                var d = p_disp ?? Application.Current?.Dispatcher;
                if (d != null)
                {
                    if (d.CheckAccess())
                    {
                        return await InnerCreateWindow();
                    }
                    else
                    {
                        return await d.InvokeAsync(() => InnerCreateWindow()).Task.Unwrap();
                    }
                }
            }
            return await InnerCreateWindow(p_useViewDispatcher == true);

            async Task<Window> InnerCreateWindow(bool p_runOnViewDisp = false)
            {
                var view = ViewLocator.LocateForModel(rootModel, null, context);
                if (p_runOnViewDisp && view.Dispatcher != null)
                {
                    var d = view.Dispatcher;
                    if (!d.CheckAccess())
                    {
                        return await d.InvokeAsync(() => CreateWindow()).Task.Unwrap();
                    }
                }
                return await CreateWindow();
                
                async Task<Window> CreateWindow()
                {
                    var window = EnsureWindow(rootModel, view, isDialog);
                    ViewModelBinder.Bind(rootModel, window, context);

                    var haveDisplayName = rootModel as IHaveDisplayName;
                    if (string.IsNullOrEmpty(window.Title) && haveDisplayName != null && !ConventionManager.HasBinding(window, Window.TitleProperty))
                    {
                        var binding = new Binding("DisplayName") { Mode = BindingMode.TwoWay };
                        window.SetBinding(Window.TitleProperty, binding);
                    }

                    ApplySettings(window, settings);

                    var conductor = new WindowConductor(rootModel, window);

                    await conductor.InitialiseAsync();

                    return window;
                }
            }

            
        }

        private Task<Window> InnerCreateWindow(bool v)
        {
            throw new System.NotImplementedException();
        }

        /// <summary>
        /// Makes sure the view is a window is is wrapped by one.
        /// </summary>
        /// <param name="model">The view model.</param>
        /// <param name="view">The view.</param>
        /// <param name="isDialog">Whethor or not the window is being shown as a dialog.</param>
        /// <returns>The window.</returns>
        protected virtual Window EnsureWindow(object model, object view, bool isDialog)
        {
            if (!(view is Window window))
            {
                window = new Window
                {
                    Content = view,
                    SizeToContent = SizeToContent.WidthAndHeight
                };

                window.SetValue(View.IsGeneratedProperty, true); 
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;                 
            }

            return window;
        }

        /// <summary>
        /// Infers the owner of the window.
        /// </summary>
        /// <param name="window">The window to whose owner needs to be determined.</param>
        /// <returns>The owner.</returns>
        protected virtual Window InferOwnerOf(Window window)
        {
            var application = Application.Current;
            if (application == null)
            {
                return null;
            }

            var active = application.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive);
            active = active ?? (PresentationSource.FromVisual(application.MainWindow) == null ? null : application.MainWindow);
            return active == window ? null : active;
        }

        /// <summary>
        /// Creates the page.
        /// </summary>
        /// <param name="rootModel">The root model.</param>
        /// <param name="context">The context.</param>
        /// <param name="settings">The optional popup settings.</param>
        /// <returns>The page.</returns>
        public virtual async Task<Page> CreatePageAsync(object rootModel, object context, IDictionary<string, object> settings)
        {
            var view = EnsurePage(rootModel, ViewLocator.LocateForModel(rootModel, null, context));
            ViewModelBinder.Bind(rootModel, view, context);

            var haveDisplayName = rootModel as IHaveDisplayName;
            if (string.IsNullOrEmpty(view.Title) && haveDisplayName != null && !ConventionManager.HasBinding(view, Page.TitleProperty))
            {
                var binding = new Binding("DisplayName") { Mode = BindingMode.TwoWay };
                view.SetBinding(Page.TitleProperty, binding);
            }

            ApplySettings(view, settings);

            if (rootModel is IActivate activator)
            {
                await activator.ActivateAsync();
            }

            if (rootModel is IDeactivate deactivatable)
            {
                view.Unloaded += async (s, e) => await deactivatable.DeactivateAsync(true);
            }

            return view;
        }

        /// <summary>
        /// Ensures the view is a page or provides one.
        /// </summary>
        /// <param name="model">The model.</param>
        /// <param name="view">The view.</param>
        /// <returns>The page.</returns>
        protected virtual Page EnsurePage(object model, object view)
        {
            if (view is Page page)
            {
                return page;
            }

            page = new Page { Content = view };
            page.SetValue(View.IsGeneratedProperty, true);

            return page;
        }

        private bool ApplySettings(object target, IEnumerable<KeyValuePair<string, object>> settings)
        {
            if (settings != null)
            {
                var type = target.GetType();

                foreach (var pair in settings)
                {
                    var propertyInfo = type.GetProperty(pair.Key);

                    if (propertyInfo != null)
                    {
                        propertyInfo.SetValue(target, pair.Value, null);
                    }
                }

                return true;
            }

            return false;
        }
    }
}
