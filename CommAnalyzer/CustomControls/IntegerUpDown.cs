using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace CommAnalyzer.CustomControls
{
    /// <summary>
    /// Realice los pasos 1a o 1b y luego 2 para usar este control personalizado en un archivo XAML.
    ///
    /// Paso 1a) Usar este control personalizado en un archivo XAML existente en el proyecto actual.
    /// Agregue este atributo XmlNamespace al elemento raíz del archivo de marcado en el que 
    /// se va a utilizar:
    ///
    ///     xmlns:MyNamespace="clr-namespace:CommAnalyzer.CustomControls"
    ///
    ///
    /// Paso 1b) Usar este control personalizado en un archivo XAML existente en otro proyecto.
    /// Agregue este atributo XmlNamespace al elemento raíz del archivo de marcado en el que 
    /// se va a utilizar:
    ///
    ///     xmlns:MyNamespace="clr-namespace:CommAnalyzer.CustomControls;assembly=CommAnalyzer.CustomControls"
    ///
    /// Tendrá también que agregar una referencia de proyecto desde el proyecto en el que reside el archivo XAML
    /// hasta este proyecto y recompilar para evitar errores de compilación:
    ///
    ///     Haga clic con el botón secundario del mouse en el proyecto de destino en el Explorador de soluciones y seleccione
    ///     "Agregar referencia"->"Proyectos"->[Busque y seleccione este proyecto]
    ///
    ///
    /// Paso 2)
    /// Prosiga y utilice el control en el archivo XAML.
    ///
    ///     <MyNamespace:IntegerUpDown/>
    ///
    /// </summary>
    [TemplatePart(Name = "DownButtonElement", Type = typeof(RepeatButton))]
    [TemplateVisualState(Name = "Positive", GroupName = "ValueStates")]
    [TemplateVisualState(Name = "Negative", GroupName = "ValueStates")]
    [TemplateVisualState(Name = "Focused3", GroupName = "FocusedStates")]
    [TemplateVisualState(Name = "Unfocused3", GroupName = "FocusedStates")]
    public class IntegerUpDown : Control
    {
        public IntegerUpDown()
        {
            DefaultStyleKey = typeof(IntegerUpDown);
            this.IsTabStop = true;
            PreviewTextInput += NumericTextBox_PreviewTextInput;
            LostFocus += IntegerUpDown_LostFocus;
        }

        private void IntegerUpDown_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Value > MaxValue)
                Value = MaxValue;
            if (Value < MinValue)
                Value = MinValue;
        }

        void NumericTextBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled = isTextAllowed(e.Text);
        }

        private static bool isTextAllowed(string text)
        {
            var regex = new Regex(@"-|[0-9]");
            return !regex.IsMatch(text);
        }

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(int), typeof(IntegerUpDown), new PropertyMetadata(new PropertyChangedCallback(ValueChangedCallback)));

        public bool IsReadOnlyTextBox { get; set; } = false;

        public int Value
        {
            get
            {
                return (int)GetValue(ValueProperty);
            }

            set
            {
                SetValue(ValueProperty, value);
            }
        }

        private static void ValueChangedCallback(DependencyObject obj,
            DependencyPropertyChangedEventArgs args)
        {
            IntegerUpDown ctl = (IntegerUpDown)obj;
            int newValue = (int)args.NewValue;

            // Call UpdateStates because the Value might have caused the
            // control to change ValueStates.
            ctl.UpdateStates(true);

            // Call OnValueChanged to raise the ValueChanged event.
            ctl.OnValueChanged(new ValueChangedEventArgs(IntegerUpDown.ValueChangedEvent, newValue));
        }

        public static readonly RoutedEvent ValueChangedEvent = EventManager.RegisterRoutedEvent("ValueChanged", RoutingStrategy.Direct, typeof(ValueChangedEventHandler), typeof(IntegerUpDown));

        public event ValueChangedEventHandler ValueChanged
        {
            add { AddHandler(ValueChangedEvent, value); }
            remove { RemoveHandler(ValueChangedEvent, value); }
        }

        protected virtual void OnValueChanged(ValueChangedEventArgs e)
        {
            // Raise the ValueChanged event so applications can be alerted
            // when Value changes.
            try
            {
                RaiseEvent(e);
            }
            catch (Exception)
            {

                throw;
            }

        }

        private void UpdateStates(bool useTransitions)
        {
            if (Value >= 0)
            {
                VisualStateManager.GoToState(this, "Positive", useTransitions);
            }
            else
            {
                VisualStateManager.GoToState(this, "Negative", useTransitions);
            }

            if (IsFocused)
            {
                VisualStateManager.GoToState(this, "Focused3", useTransitions);
            }
            else
            {
                VisualStateManager.GoToState(this, "Unfocused3", useTransitions);
            }
        }

        public override void OnApplyTemplate()
        {
            UpButtonElement = GetTemplateChild("UpButton") as RepeatButton;
            DownButtonElement = GetTemplateChild("DownButton") as RepeatButton;
            UpdateStates(false);
        }

        private RepeatButton downButtonElement;

        private RepeatButton DownButtonElement
        {
            get
            {
                return downButtonElement;
            }

            set
            {
                if (downButtonElement != null)
                {
                    downButtonElement.Click -= new RoutedEventHandler(downButtonElement_Click);
                }
                downButtonElement = value;

                if (downButtonElement != null)
                {
                    downButtonElement.Click += new RoutedEventHandler(downButtonElement_Click);
                }
            }
        }

        void downButtonElement_Click(object sender, RoutedEventArgs e)
        {
            if (Value > MinValue)
                Value--;
        }

        private RepeatButton upButtonElement;

        private RepeatButton UpButtonElement
        {
            get
            {
                return upButtonElement;
            }

            set
            {
                if (upButtonElement != null)
                {
                    upButtonElement.Click -= new RoutedEventHandler(upButtonElement_Click);
                }
                upButtonElement = value;

                if (upButtonElement != null)
                {
                    upButtonElement.Click += new RoutedEventHandler(upButtonElement_Click);
                }
            }
        }

        public int MaxValue { get; set; } = 255;
        public int MinValue { get; set; } = 1;

        void upButtonElement_Click(object sender, RoutedEventArgs e)
        {
            if (Value < MaxValue)
                Value++;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();
        }

        protected override void OnGotFocus(RoutedEventArgs e)
        {
            base.OnGotFocus(e);
            UpdateStates(true);
        }

        protected override void OnLostFocus(RoutedEventArgs e)
        {
            base.OnLostFocus(e);

            if (Value > MaxValue)
                Value = MaxValue;
            if (Value < MinValue)
                Value = MinValue;

            UpdateStates(true);
        }
    }

    public delegate void ValueChangedEventHandler(object sender, ValueChangedEventArgs e);

    public class ValueChangedEventArgs : RoutedEventArgs
    {
        private int _value;

        public ValueChangedEventArgs(RoutedEvent id, int num)
        {
            _value = num;
            RoutedEvent = id;
        }

        public int Value
        {
            get { return _value; }
        }
    }
}
