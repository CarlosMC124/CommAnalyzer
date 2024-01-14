using CommAnalyzer.Helpers;
using CommAnalyzer.Helpers.DNP3;
using CommAnalyzer.Themes;
using System.IO.Ports;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static CommAnalyzer.Helpers.CSoporte;
using static CommAnalyzer.Helpers.DNP3.DNP3Support;

namespace CommAnalyzer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Atributos
        bool IsWindowsNormal = true;
        double _Top;
        double _Left;
        double _Height;
        double _Width;
        #endregion

        #region Atributos IEC

        public enum LogType
        {
            RX,
            TX,
            RX_ERR,
            TX_ERR
        }

        public enum IECProtocolType
        {
            IEC104 = 0,
            IEC101_B = 1,
            IEC101_U = 2
        };


        IECProtocolType IECtype;

        // ASDU IEC104
        //  -------------------------------------------------------------------------------------------------     
        // |  typeID  |    VSQ    |    COT    |   Common Address   |    Info Object #1     |   #2   |   #3   |   
        //  -------------------------------------------------------------------------------------------------  
        //    1 bytes   2 bytes     1-2 bytes        1-2 bytes        (1-2-3 bytes) + nbytes


        public int LinkAddress = 1;
        public int ASDUAddress = 1;
        public int OAddress = 0;
        public int LinkAddressLength = 1;
        public int COTAddressLength = 1;        //SizeOfCOT
        public int ASDUAddressLength = 2;       //SizeOfCA
        public int IOAAddressLength = 3;        //SizeOfIOA
                                                //Usar esta condicion para agregar el IOA
        /*
        1 octeto: direcciones entre 1 y 255.
        2 octeto: direcciones entre 1 y 65.535
        3 octeto: direcciones entre 1 y 16.777.215
        */

        int K = 12;
        int W = 8;
        int T0 = 10;
        int T1 = 15;
        int T2 = 10;
        int T3 = 20;


        //General State
        bool IsConnected = false;

        #endregion

        #region Atributos SERIAL TCP
        SerialPort sp = new SerialPort();
        #endregion


        #region Delegate Declarations
        public delegate void GUIEscribirReceiveSendData(string paramString, LogType logtype);
        public delegate void GUIUpdateCommunication();
        public delegate void GUILimpiarLog();
        #endregion

        #region Delegate Functions
        public void DoGUIEscribirReceiveSendData(string paramString, LogType logtype)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(new GUIEscribirReceiveSendData(DoGUIEscribirReceiveSendData), paramString, logtype);
                return;
            }
            //Mostrar mensaje enviado
            //AgregarLogRXTX(DateTime.Now.ToString("HH:mm:ss.fff") + " " + paramString, logtype);
            AgregarLogRXTX(paramString, logtype);
        }

        public void DoGUIUpdateCommunication()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(new GUIUpdateCommunication(DoGUIUpdateCommunication));
                return;
            }
            UpdateCountBuffer();
        }

        public void DoGUILimpiarLog()
        {
            if (!Dispatcher.CheckAccess()) // CheckAccess returns true if you're on the dispatcher thread
            {
                Dispatcher.Invoke(new GUILimpiarLog(DoGUILimpiarLog));
                return;
            }
            LimpiarLog();
        }
        #endregion

        #region Frame & Buffer mensaje
        int numFrame = 0;
        int numBuffer_Recv = 0;
        int numBuffer_Send = 0;
        bool SuspendMonitoring = false;

        FixedSizeQueue<string> bMessage = new FixedSizeQueue<string>(1000);
        #endregion

        public MainWindow()
        {
            InitializeComponent();

            CargarComboBox_SerialConfiguration();
            CargarComboBox_TCPConfiguration();

            rbSerial.IsChecked = true;  //Inicia seleccionando IEC104
            rbDNP3.IsChecked = true;  //Inicia seleccionando IEC104


        }

        //https://www.codeproject.com/Questions/1220205/Serialport-receives-data-as-part
        //You are handling receiving in an asynchronous event. That is triggered when new data are available. In your case port.BytesToRead is one with the first event. Once the event has been handled (one byte is read) it returns. Meanwhile additional bytes has been received and the event has been triggered again.
        //A possible solution is using a class global buffer and a buffer index variable so that data can be appended.Once the number of expected data bytes is received (a fixed value like it seems in your case, length is part of data, or there is an end indicator like a new line with text data), process the data(set text) and reset the index variable.

        static int search(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                if (match(haystack, needle, i))
                {
                    return i;
                }
            }
            return -1;
        }

        static bool match(byte[] haystack, byte[] needle, int start)
        {
            if (needle.Length + start > haystack.Length)
            {
                return false;
            }
            else
            {
                for (int i = 0; i < needle.Length; i++)
                {
                    if (needle[i] != haystack[i + start])
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        //
        int index;
        int TotalByte;
        byte[] bufferBytes = new byte[0];

        bool validLinkHeader = false;
        byte[] arrayLH = new byte[8];
        byte[] arrayCRC = new byte[2];

        public int countRealBytes(int countByteToRead)
        {
            int countByteSegment;
            int countRealByte = 0;
            countByteSegment = countByteToRead - 5;

            //Bytes of Segments
            countRealByte += (countByteSegment / 16) * 18;      // (18: 16 + 2 "CRC")

            if (countByteSegment % 16 > 0)
                countRealByte += (countByteSegment % 16) + 2;  // (Bytes + 2 "CRC")
                                                               //else 0 o simplemente no suma nada

            //Bytes of LinkLayer
            countRealByte += 10;
            return countRealByte;
        }
        public byte[] AddByteToArray(byte[] bArray, byte[] newByte)
        {
            byte[] newArray = new byte[bArray.Length + newByte.Length];
            bArray.CopyTo(newArray, 0);
            newByte.CopyTo(newArray, bArray.Length);
            return newArray;
        }

        public void spReceiveData(object sender, SerialDataReceivedEventArgs e)
        {
            int _bytesToRead = sp.BytesToRead;
            byte[] recvData = new byte[_bytesToRead];
            sp.Read(recvData, 0, _bytesToRead);

            //_buffer += sp.ReadExisting();   //READEXISTING FALLA CUANDO DEBE LEER C1: Á, lo lee como ? que es 3F:63 y FALLA EL CRC, no se porque no funciona
            //byte[] bufferBytes = Encoding.ASCII.GetBytes(recvData);
            bufferBytes = AddByteToArray(bufferBytes, recvData);

            /* PRUEBA DEL ADD ARRAY
            byte[] aa = new byte[3] { 0x01,0x02,0x03};
            byte[] bb = new byte[2] { 0xAA,0xBB};
            aa = AddByteToArray(aa, bb);
            */

            if (validLinkHeader == false)
            {
                byte[] linkHeader = new byte[2] { 0x05, 0x64 };
                index = search(bufferBytes, linkHeader);    //Tengo un posible index, pero quizas el largo solo es 2 bytes, y minimo se necesita 10 del LinkHeader

                if (index >= 0 && bufferBytes.Length >= index + 10)
                {
                    int countByteToRead = bufferBytes[index + 2];
                    TotalByte = countRealBytes(countByteToRead);   //Byte que registra la cantidad de bytes [05-FF]
                    Array.Copy(bufferBytes, index, arrayLH, 0, 8);
                    GeneralCRC(arrayLH, ref arrayCRC, 0xA6BC, 0x0000, 0x0000, 0xFFFF);
                    bool CRCok = arrayCRC[0] == bufferBytes[index + 8] && arrayCRC[1] == bufferBytes[index + 9];

                    if ((countByteToRead >= 5 && countByteToRead <= 255) && CRCok)
                    {
                        validLinkHeader = true;
                    }
                    else
                    {
                        //validLinkHeader = true;
                        /* string b = _buffer.Substring(index + 10, _buffer.Length - index - 10);

                         _buffer = b;*/
                    }

                }
            }

            if (bufferBytes.Length >= index + TotalByte && validLinkHeader)
            {
                byte[] a = new byte[TotalByte];
                Array.Copy(bufferBytes, index, a, 0, TotalByte);

                byte[] b = new byte[bufferBytes.Length - TotalByte - index];
                Array.Copy(bufferBytes, index + TotalByte, b, 0, b.Length);

                bufferBytes = b;    //Recortamos el bytearray para que reinicie la cuenta

                string GPSrxStringhex = BitConverter.ToString(a); //https://stackoverflow.com/q/22355593
                numFrame++;
                validLinkHeader = false;

                string message = string.Empty;
                byte DLC = a[3];
                if ((DLC & 0x80) != 0)  //bit 7 DLC: DIR
                    message = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff") + "\tTx =>  " + GPSrxStringhex;
                else
                    message = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff") + "\tRx <=  " + GPSrxStringhex;

                if (SuspendMonitoring == false)
                {
                    if ((DLC & 0x80) != 0)  //bit 7 DLC: DIR
                        DoGUIEscribirReceiveSendData(message, LogType.TX);  //MASTER
                    else
                        DoGUIEscribirReceiveSendData(message, LogType.RX);  //OUTSTATION
                }
                else
                {
                    //BufferLogData(message);
                    bMessage.Enqueue(message);
                    numBuffer_Recv++;
                }
                DoGUIUpdateCommunication();
            }
            else
            {

            }
        }


        public void DataAnalysis(byte[] byteMessage)
        {
            DNP3Message dnp3Message = new DNP3Message(byteMessage);
        }

        #region INTENTOS FALLIDOS DE LEER POR SERIAL
        /*
      private static string _buffer = string.Empty;
      public void spReceiveDataFF(object sender, SerialDataReceivedEventArgs e)
      {
          //data = _buffer + data;
          _buffer += sp.ReadExisting();   //READEXISTING FALLA CUANDO DEBE LEER C1: Á, lo lee como ? que es 3F:63 y FALLA EL CRC, no se porque no funciona
          byte[] bufferBytes = Encoding.ASCII.GetBytes(_buffer);

          if (validLinkHeader == false)
          {
              byte[] linkHeader = new byte[2] { 0x05, 0x64 };
              index = search(bufferBytes, linkHeader);    //Tengo un posible index, pero quizas el largo solo es 2 bytes, y minimo se necesita 10 del LinkHeader

              if (index >= 0 && bufferBytes.Length >= index + 10)
              {
                  int countByteToRead =  bufferBytes[index + 2];
                  TotalByte = countRealBytes(bufferBytes[index + 2]);   //Byte que registra la cantidad de bytes [05-FF]
                  Array.Copy(bufferBytes, index, arrayLH, 0, 8);
                  GeneralCRC(arrayLH, ref arrayCRC, 0xA6BC, 0x0000, 0x0000, 0xFFFF);
                  bool CRCok = arrayCRC[0] == bufferBytes[index + 8] && arrayCRC[1] == bufferBytes[index + 9];

                  if ((countByteToRead > 5 && countByteToRead < 255) && !CRCok)
                  {
                      validLinkHeader = true;
                  }
                  else
                  {
                      //validLinkHeader = true;
                      string b = _buffer.Substring(index+10, _buffer.Length - index-10);

                      _buffer = b;
                  }

              }
          }

          if (bufferBytes.Length >= index + TotalByte && validLinkHeader)
          {
              string a = _buffer.Substring(index, TotalByte - 1);
              string b = _buffer.Substring(TotalByte, _buffer.Length - TotalByte);


              byte[] bufferByte2s = Encoding.ASCII.GetBytes(a);
              _buffer = b;

              string GPSrxStringhex = BitConverter.ToString(bufferByte2s); //https://stackoverflow.com/q/22355593
              numFrame++;
              validLinkHeader = false;
              string message = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff") + " " + GPSrxStringhex;
              if (SuspendMonitoring == false)
              {
                  //if (message.Contains("Rx"))
                  DoGUIEscribirReceiveSendData(message, LogType.RX);
              }
              else
              {
                  //BufferLogData(message);
                  bMessage.Enqueue(message);
                  numBuffer_Recv++;
              }
              DoGUIUpdateCommunication();
          }
          else
          {

          }
      }
      */

        /*
        public void spReceiveData33(object sender, SerialDataReceivedEventArgs e)
        {
            //data = _buffer + data;
            _buffer += sp.ReadExisting();
            byte[] bufferBytes = Encoding.ASCII.GetBytes(_buffer);

            if (processData == false)
            {
                byte[] linkHeader = new byte[2] { 0x05, 0x64 };


                index = search(bufferBytes, linkHeader);

                if (index >= 0) { processData = true; }

                countByteToRead = bufferBytes[index + 2];   //Byte que registra la cantidad de bytes [05-FF]

                countRealByte = 0;
                countByteSegment = countByteToRead - 5;

                //Bytes of Segments
                countRealByte += (countByteSegment / 16) * 18;      // (18: 16 + 2 "CRC")

                if (countByteSegment % 16 > 0)
                    countRealByte += (countByteSegment % 16) + 2;  // (Bytes + 2 "CRC")
                                                                   //else 0 no suma nada

                //Bytes of LinkLayer
                countRealByte += 10;
            }

            if (bufferBytes.Length >= countRealByte)
            {
                string a = _buffer.Substring(index, countRealByte - 1);
                string b = _buffer.Substring(countRealByte, _buffer.Length - countRealByte);


                byte[] bufferByte2s = Encoding.ASCII.GetBytes(a);
                _buffer = b;

                string GPSrxStringhex = BitConverter.ToString(bufferByte2s); //https://stackoverflow.com/q/22355593
                numFrame++;
                processData = false;
                string message = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff") + " " + GPSrxStringhex;
                if (SuspendMonitoring == false)
                {
                    //if (message.Contains("Rx"))
                    DoGUIEscribirReceiveSendData(message, LogType.RX);
                }
                else
                {
                    //BufferLogData(message);
                    bMessage.Enqueue(message);
                    numBuffer_Recv++;
                }
                DoGUIUpdateCommunication();
            }
            else
            {

            }

            //int index = _buffer.IndexOf('\n');
            //int start = 0;

            //while (index != -1)
            //{
            //    var command = data.Substring(start, index - start);
            //    ProcessCommand(command.TrimEnd('\r'));
            //    start = index + 1;
            //    index = data.IndexOf('\n', start);
            //}

            //// Store the leftover in the buffer
            //if (!data.EndsWith("\n"))
            //{
            //    _buffer = data.Substring(start);
            //}
            //else
            //{
            //    _buffer = string.Empty;
            //}
        }
        */

        /*
        private string buffer { get; set; }
        public void spReceiveData4(object sender, SerialDataReceivedEventArgs e)
        {
            buffer = string.Empty;
            buffer += sp.ReadExisting();
            string GPSrxStringhex = BitConverter.ToString(Encoding.ASCII.GetBytes(buffer)); //https://stackoverflow.com/q/22355593


            numFrame++;
            string message = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff") + " " + GPSrxStringhex;
            if (SuspendMonitoring == false)
            {
                //if (message.Contains("Rx"))
                DoGUIEscribirReceiveSendData(message, LogType.RX);
            }
            else
            {
                //BufferLogData(message);
                bMessage.Enqueue(message);
                numBuffer_Recv++;
            }
            DoGUIUpdateCommunication();

        }
        */

        /*
        public void spReceiveData33(object sender, SerialDataReceivedEventArgs e)
        {

            System.Threading.Thread.Sleep(30);
            SerialPort _SerialPort = (SerialPort)sender;
            int _bytesToRead = _SerialPort.BytesToRead;
            byte[] recvData = new byte[_bytesToRead];
            _SerialPort.Read(recvData, 0, _bytesToRead);
            string hexString = BitConverter.ToString(recvData).Replace("-", " "); ; //https://stackoverflow.com/a/623184

            numFrame++;
            string message = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff") + " " + hexString;
            if (SuspendMonitoring == false)
            {
                //if (message.Contains("Rx"))
                DoGUIEscribirReceiveSendData(message, LogType.RX);
            }
            else
            {
                //BufferLogData(message);
                bMessage.Enqueue(message);
                numBuffer_Recv++;
            }
            DoGUIUpdateCommunication();
            //this.BeginInvoke(new SetTextDeleg(UpdateUI), new object[] { recvData });
        }
        private void UpdateUI(byte[] data)
        {
            string str = BitConverter.ToString(data);
            //textBox1.Text += str;
        }
        */

        /*
        private bool dataReceived = false;
        private byte[] readBuffer = new byte[2094];
        private DateTime lastReceive;
        private int nextSign = 0;

        private void spReceiveData2(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort _SerialPort = (SerialPort)sender;

            float silence = 4000 / Convert.ToInt32(_SerialPort.BaudRate);
            if ((DateTime.Now.Ticks - lastReceive.Ticks) > TimeSpan.TicksPerMillisecond * silence)
                nextSign = 0;


            int _bytesToRead = _SerialPort.BytesToRead;

            byte[] recvData = new byte[_bytesToRead];
            _SerialPort.Read(recvData, 0, _bytesToRead);

            //sp.Read(recvData, 0, _bytesToRead);

            Array.Copy(recvData, 0, readBuffer, nextSign, recvData.Length);
            lastReceive = DateTime.Now;
            nextSign = _bytesToRead + nextSign;


            //string hexString = sp.ReadExisting();


            //Do something here
            string hexString = BitConverter.ToString(recvData).Replace("-", " "); ; //https://stackoverflow.com/a/623184
            nextSign = 0;

            numFrame++;
            string message = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss.fff") + " " + hexString;
            if (SuspendMonitoring == false)
            {
                //if (message.Contains("Rx"))
                DoGUIEscribirReceiveSendData(message, LogType.RX);
            }
            else
            {
                //BufferLogData(message);
                bMessage.Enqueue(message);
                numBuffer_Recv++;
            }
            DoGUIUpdateCommunication();
        }
        */
        #endregion



        #region SECCION BOTONES DE VENTANA
        private void btnMinimizar_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void btnMaximizarRestaurar_Click(object sender, RoutedEventArgs e)
        {
            if (IsWindowsNormal)    //Al inicio es normal
            {
                _Top = this.Top;    //GUARDAMOS EN MEMORIA
                _Left = this.Left;
                _Height = this.Height;
                _Width = this.Width;

                this.Width = SystemParameters.WorkArea.Width;
                this.Height = SystemParameters.WorkArea.Height;
                this.Left = 0;
                this.Top = 0;
                this.WindowState = WindowState.Normal;

                IsWindowsNormal = false;
            }
            else
            {
                this.Top = _Top;
                this.Left = _Left;
                this.Height = _Height;
                this.Width = _Width;

                IsWindowsNormal = true;
            }
        }

        private void btnExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void gMenu_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void cbSelectTheme_DropDownClosed(object sender, EventArgs e)
        {
            var aa = (SolidColorBrush)((System.Windows.Controls.Border)(cbSelectTheme.SelectedItem)).Background;

            if (aa.ToString() == "#FFF2F2F2")
                ThemesController.SetTheme(ThemesController.ThemeTypes.Light);
            else if (aa.ToString() == "#FF505050")
                ThemesController.SetTheme(ThemesController.ThemeTypes.Dark);
            else
                ThemesController.SetTheme(ThemesController.ThemeTypes.Light);


            //TODO se 
            ActualizarIconbtnViewConfiguration();   //Para forzar a actualizar el cambio de color en el check ya que programatically se ha hecho la seleccion del icono, se debe hacer luego del cambio
        }

        private void ActualizarIconbtnViewConfiguration()
        {
            System.Windows.Shapes.Path myPath = new System.Windows.Shapes.Path();
            myPath.Stretch = Stretch.Uniform;
            var brush2 = Application.Current.Resources["PrimaryTextColor"];
            myPath.Fill = (Brush)brush2;

            myPath.Height = 15;
            myPath.Width = 15;


            if (gConfiguration.Visibility == Visibility.Visible)
            {
                myPath.Data = (PathGeometry)FindResource("check");
            }
            else
            {
                myPath.Data = (PathGeometry)FindResource("none");
            }

            //Referencia:https://stackoverflow.com/q/40016699/15334395
            // myPath.Fill = "{DynamicResource PrimaryTextColor}"  Margin = "0 0 0 0"
            btnView_Configuration.Icon = myPath; //menExit.Icon = (StreamGeometry)FindResource("ImgExit"); 
        }

        #endregion

        #region SECCION BOTONES DE MENU
        private void Open_Click(object sender, RoutedEventArgs e)
        {

        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {

        }

        private void btnView_Configuration_Click(object sender, RoutedEventArgs e)
        {
            gConfiguration.Visibility = (gConfiguration.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
            ActualizarIconbtnViewConfiguration();
        }

        private void btnView_Propierties_Click(object sender, RoutedEventArgs e)
        {

        }
        #endregion

        #region SECCION CONFIGURACION

        #region GENERAL
        private void btnCloseConfig_Click(object sender, RoutedEventArgs e)
        {
            gConfiguration.Visibility = Visibility.Collapsed;
            ActualizarIconbtnViewConfiguration();
        }
        #region PROTOCOL
        private void rbDNP3_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void rbIEC_Checked(object sender, RoutedEventArgs e)
        {
            //TODO HACE APARECER Y DESPARECER LOS DEMAS CONTROLES ESPECIFICOS
        }

        private void rbModbus_Checked(object sender, RoutedEventArgs e)
        {
            //HACE APARECER PARA ESCOGER EL TIPO DE MODBUS PARA TCPIP: OVERTCP, TCP, PARA SERIAL ASCII o RTU
        }

        #endregion

        #region SERIAL-TCPIP
        private void rbTCPIP_Checked(object sender, RoutedEventArgs e)
        {
            ActualizarTipoVisibilidad();
        }

        private void rbSerial_Checked(object sender, RoutedEventArgs e)
        {
            ActualizarTipoVisibilidad();
        }

        private void ActualizarTipoVisibilidad()
        {
            if (rbTCPIP.IsChecked == true)
            {
                spSerial.Visibility = Visibility.Collapsed;
                spTCPIP.Visibility = Visibility.Visible;
                //spLinkAddress.Visibility = Visibility.Collapsed;               
            }
            else
            {
                spSerial.Visibility = Visibility.Visible;
                spTCPIP.Visibility = Visibility.Collapsed;
                //spLinkAddress.Visibility = Visibility.Visible;               
            }
        }
        #endregion

        #endregion

        #region SERIAL
        private void btnUpdateSerialPort_Click(object sender, RoutedEventArgs e)
        {
            cbPuertoCOM.ItemsSource = CSoporte.PuertoSerieNombreCompleto();
        }

        private void CargarComboBox_SerialConfiguration()
        {
            //1) Puerto accesibles:            
            cbPuertoCOM.ItemsSource = CSoporte.PuertoSerieNombreCompleto();
            cbPuertoCOM.SelectedValuePath = "Key";      //(COM6)
            cbPuertoCOM.DisplayMemberPath = "Value";    //Prolific USB-to-Serial Comm Port (COM6)

            //2) Baud Rates:
            string[] baudrates = { "300", "600", "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200", "128000", "230400", "256000" };
            foreach (string baudrate in baudrates)
                cbBaudRate.Items.Add(baudrate);
            //cbBaudRate.SelectedIndex = 5;   //9600
            cbBaudRate.SelectedIndex = 9;   //115200

            //3) Bits de Datos:
            string[] bitsdedatos = { "7", "8" };
            foreach (string bitsdedato in bitsdedatos)
                cbNumBits.Items.Add(bitsdedato);
            cbNumBits.SelectedIndex = 1;

            //4) Bits de Parada:
            cbBitsParada.ItemsSource = Enum.GetValues(typeof(System.IO.Ports.StopBits));
            cbBitsParada.SelectedIndex = 1;

            //5) Paridad:
            cbParidad.ItemsSource = Enum.GetValues(typeof(System.IO.Ports.Parity));
            //cbParidad.SelectedIndex = 2; //Even
            cbParidad.SelectedIndex = 0; //None

            //7) Control Flow: DTR y RTS
            chbDTR.IsChecked = false;
            chbRTS.IsChecked = false;
        }


        #endregion

        #region TCPIP
        private void btnUpdateIPAddress_Click(object sender, RoutedEventArgs e)
        {
            CargarComboBox_TCPConfiguration();
        }

        private void tbTCPPort_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !isTextAllowed(e.Text);
        }
        private bool isTextAllowed(string text)
        {
            return (new Regex(@"[0-9]")).IsMatch(text);
        }

        private void btnAddIPAddress_Click(object sender, RoutedEventArgs e)
        {
            //VALIDAR LA IP Y MASK, VER QUE NO EXISTA YA, SINO INDICAR QUE ESTA REPETIDA
            string patronIP = @"\b(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b";

            var leadingOnes = "(255|254|252|248|240|224|192|128|0+)";
            var allOnes = @"(255\.)";
            var patronMask = new Regex("^((" + allOnes + "{3}" + leadingOnes + ")|" +
                                       "(" + allOnes + "{2}" + leadingOnes + @"\.0+)|" +
                                       "(" + allOnes + leadingOnes + @"(\.0+){2})|" +
                                       "(" + leadingOnes + @"(\.0+){3}))$");

            var ValidNewIP = false;
            var ValidNewMask = false;
            if (tbNewIP.Address == "" || tbNewIP.Address == null)
            {
                MessageBox.Show("Put an ip");
                tbNewIP.Focus();
            }
            else if (Regex.IsMatch(tbNewIP.Address, patronIP))
            {
                //Si es valido verificar que no repetita
                if (cbIPAddress.Items.Contains(tbNewIP.Address))
                {
                    MessageBox.Show("There is an existing ip");
                    tbNewIP.Focus();
                }
                else
                {
                    ValidNewIP = true;
                }

            }
            else
            {
                MessageBox.Show("Put a valid ip");
                tbNewIP.Focus();
            }

            if (tbNewMask.Address == "" || tbNewMask.Address == null)
            {
                MessageBox.Show("Put a mask");
                tbNewMask.Focus();
            }
            else if (patronMask.IsMatch(tbNewMask.Address))
            {
                ValidNewMask = true;
            }
            else
            {
                MessageBox.Show("Put a valid mask");
                tbNewMask.Focus();
            }

            if (ValidNewIP && ValidNewMask)
            {
                setIP();
                CargarComboBox_TCPConfiguration();
            }
        }

        public void setIP()
        {
            //Se necesita que se ejecute como Administrador, Manifest:
            //https://ourcodeworld.com/articles/read/889/how-to-force-a-csharp-based-winforms-applications-to-run-with-administrator-rights-on-any-environment

            //https://www.codeproject.com/Questions/5228478/How-to-ADD-IP-address-in-windows-10-using-Csharp
            //string myDesc = "Realtek USB GbE Family Controller";
            //string gateway = "10.210.255.1";
            //string subnetMask = "255.255.255.0";
            //string address = "10.210.255.102";

            if (cbNIC.Text == string.Empty)
            {
                MessageBox.Show("Select a NIC Adapter");
            }
            else
            {
                var adapterConfig = new ManagementClass("Win32_NetworkAdapterConfiguration");
                var networkCollection = adapterConfig.GetInstances();


                foreach (ManagementObject adapter in networkCollection)
                {
                    string description = adapter["Description"] as string;

                    //Se tuvo que sacar la NIC con el nombre y descripcion para comprarlo
                    var NICSelectedDescription = ((CSoporte.ComboItem)cbNIC.SelectedValue).Key.ToString();
                    var NICSelectedName = cbNIC.SelectedValue.ToString();
                    List<string> Actual_ip = new List<string>();
                    List<string> Actual_mask = new List<string>();
                    foreach (NetworkInterface netInterface in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (NICSelectedName == netInterface.Name)
                        {
                            IPInterfaceProperties ipProps = netInterface.GetIPProperties();
                            //Identify IPv4 addresses using: addr.Address.AddressFamily == AddressFamily.InterNetwork 
                            foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses)
                            {
                                if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                                {
                                    Actual_ip.Add(addr.Address.ToString());
                                    Actual_mask.Add(addr.IPv4Mask.ToString());
                                }
                            }
                        }
                    }


                    if (string.Compare(description, NICSelectedDescription, StringComparison.InvariantCultureIgnoreCase) == 0)
                    {
                        try
                        {
                            // Set IPAddress and Subnet Mask
                            var newAddress = adapter.GetMethodParameters("EnableStatic");

                            Actual_ip.Add(tbNewIP.Address);
                            Actual_mask.Add(tbNewMask.Address);
                            newAddress["IPAddress"] = Actual_ip.Select(i => i.ToString()).ToArray();
                            newAddress["SubnetMask"] = Actual_mask.Select(i => i.ToString()).ToArray();

                            adapter.InvokeMethod("EnableStatic", newAddress, null);

                            Console.WriteLine("Updated to static IP address!");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Unable to Set IP : " + ex.Message);
                        }
                    }
                }
            }
        }

        private void CargarComboBox_TCPConfiguration()
        {
            //1) Direcciones IPs
            List<string> listIPAddress = new List<string>();
            foreach (NetworkInterface netInterface in NetworkInterface.GetAllNetworkInterfaces())
            {

                IPInterfaceProperties ipProps = netInterface.GetIPProperties();
                foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        listIPAddress.Add(addr.Address.ToString());
                    }
                }
            }
            cbIPAddress.ItemsSource = listIPAddress;

            //2) NIC (Network adapates con IPs, noloopback)
            List<ComboItem> listNICComboItem = new List<ComboItem>();
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType != NetworkInterfaceType.Loopback && //lo malo es que no detecta si hay un NIC habilitado pero sin conexion de cable, lo detecta como down y no lo cuenta
                        nic.GetPhysicalAddress().ToString() != "" &&
                        nic.Speed > -1)
                {
                    ComboItem NICComboItem = new ComboItem();   //Se tiene que crear de esta forma para poder Comparar Description(key) con la libreria ManagementClass
                    NICComboItem.Value = nic.Name;      //Ethernet
                    NICComboItem.Key = nic.Description; //Intel(R) Ethernet Connection I217-V
                    listNICComboItem.Add(NICComboItem);
                }
            }
            cbNIC.ItemsSource = listNICComboItem;
        }

        #endregion

        #region IEC101-104
        private void udLinkAddress_ValueChanged(object sender, CustomControls.ValueChangedEventArgs e)
        {
            LinkAddress = udLinkAddress.Value;  //TODO PARA IEC101, el Link debe actualizar algo
        }

        private void rbLinkAdd_1B_Checked(object sender, RoutedEventArgs e)
        {
            udLinkAddress.IsEnabled = true;
            LinkAddressLength = 1;
            udLinkAddress.MaxValue = 254;
            if (udLinkAddress.Value > 254)
                udLinkAddress.Value = 254;
        }

        private void rbLinkAdd_2B_Checked(object sender, RoutedEventArgs e)
        {
            udLinkAddress.IsEnabled = true;
            LinkAddressLength = 2;
            udLinkAddress.MaxValue = 65535;
            if (udLinkAddress.Value > 65535)
                udLinkAddress.Value = 65535;
        }

        private void rbLinkAdd_N_Checked(object sender, RoutedEventArgs e)
        {
            udLinkAddress.IsEnabled = false;
            LinkAddressLength = 0;
            udLinkAddress.Value = 0;
        }

        private void rbCASDUAdd_1B_Checked(object sender, RoutedEventArgs e)
        {
            ASDUAddressLength = 1;
            //alParams.SizeOfCA = ASDUAddressLength;
        }

        private void rbCASDUAdd_2B_Checked(object sender, RoutedEventArgs e)
        {
            ASDUAddressLength = 2;
            //alParams.SizeOfCA = ASDUAddressLength;
        }

        private void rbIOAAdd_1B_Checked(object sender, RoutedEventArgs e)
        {
            IOAAddressLength = 1;
            //alParams.SizeOfIOA = IOAAddressLength;
        }

        private void rbIOAAdd_2B_Checked(object sender, RoutedEventArgs e)
        {
            IOAAddressLength = 2;
            //alParams.SizeOfIOA = IOAAddressLength;
        }

        private void rbIOAAdd_3B_Checked(object sender, RoutedEventArgs e)
        {
            IOAAddressLength = 3;
            //alParams.SizeOfIOA = IOAAddressLength;
        }

        private void chbCOTwithOrig_Checked(object sender, RoutedEventArgs e)
        {
            udCOTwithOrig.Visibility = Visibility.Visible;
            COTAddressLength = 2;
        }

        private void chbCOTwithOrig_Unchecked(object sender, RoutedEventArgs e)
        {
            udCOTwithOrig.Visibility = Visibility.Hidden;
            udCOTwithOrig.Value = 0;
            OAddress = 0;
            COTAddressLength = 1;
        }

        private void udCOTwithOrig_ValueChanged(object sender, CustomControls.ValueChangedEventArgs e)
        {
            OAddress = udCOTwithOrig.Value;
        }

        #endregion

        #region Start/Stop
        private void rbIniciar_Checked(object sender, RoutedEventArgs e)
        {
            if (IniciarRecibirDatos())
                this.Habilitar(false);
            else
                rbIniciar.IsChecked = false;
        }

        private void rbDetener_Checked(object sender, RoutedEventArgs e)
        {
            this.DetenerPuleo();
            this.Habilitar(true);
        }

        private bool IniciarRecibirDatos()
        {
            //TODO EVALUAR CON CASE, LOS PROTOCOLOS Y EL TIPO DE DATOS SERIAL O NO
            sp.PortName = cbPuertoCOM.SelectedValue.ToString();
            sp.BaudRate = Convert.ToInt32(cbBaudRate.SelectedItem.ToString());
            sp.DataBits = Convert.ToInt32(cbNumBits.SelectedItem);
            sp.Parity = (System.IO.Ports.Parity)cbParidad.SelectedItem;
            sp.StopBits = (System.IO.Ports.StopBits)cbBitsParada.SelectedItem;
            sp.ReadTimeout = 1000;
            sp.WriteTimeout = 1000;
            sp.ReceivedBytesThreshold = 8;//500000;
            sp.ReadBufferSize = 4096;//1048576;
                                     // Asignamos XonXoff
            if (false)
                sp.Handshake = Handshake.XOnXOff;

            // Asignamos DTR(Data Termial Ready) | RTS(Request to Send)
            sp.DtrEnable = (chbDTR.IsChecked == true);
            sp.RtsEnable = (chbRTS.IsChecked == true);



            if (sp.IsOpen == false) //if not open, open the port
            {
                sp.Open();
                sp.DataReceived += new SerialDataReceivedEventHandler(spReceiveData);
                return true;
            }
            {
                return false;
            }

        }

        private void DetenerPuleo()
        {
            //do your work here
            //TODO EVALUAR CON CASE, LOS PROTOCOLOS Y EL TIPO DE DATOS SERIAL O NO
            sp.Close();
        }

        private void Habilitar(bool valor)
        {
            // General Configuration
            this.rbDNP3.IsEnabled = valor;
            this.rbIEC.IsEnabled = valor;
            this.rbMobus.IsEnabled = valor;

            // Serial Configuration
            this.cbPuertoCOM.IsEnabled = valor;
            this.btnUpdateSerialPort.IsEnabled = valor;
            this.cbBaudRate.IsEnabled = valor;
            this.cbNumBits.IsEnabled = valor;
            this.cbBitsParada.IsEnabled = valor;
            this.cbParidad.IsEnabled = valor;
            this.chbDTR.IsEnabled = valor;
            this.chbRTS.IsEnabled = valor;

            // TCP/IP Configuration
            this.cbIPAddress.IsEnabled = valor;
            this.btnUpdateIPAddress.IsEnabled = valor;
            this.tbTCPPort.IsEnabled = valor;
            this.btnAddIPAddress.IsEnabled = valor;
            this.cbNIC.IsEnabled = valor;
            this.tbNewIP.IsEnabled = valor;
            this.tbNewMask.IsEnabled = valor;

            // Mode configuration
            //TODO DEPENDE DEL PROTOCOLO
            //TODO PARA MODBUS
            //this.rbRTU.IsEnabled = valor;
            //this.rbASCII.IsEnabled = valor;
        }
        #endregion

        #endregion


        #region COMMUNICATION ANALIZER
        private void chbSuspendM_Checked(object sender, RoutedEventArgs e)
        {
            //Almacena en el buffer
            SuspendMonitoring = true;
        }

        private void chbSuspendM_Unchecked(object sender, RoutedEventArgs e)
        {
            //Vacea el buffer y ademas continua el proceso normal
            SuspendMonitoring = false;

            while (bMessage._queue.Count != 0)
            {
                var message = bMessage._queue.Dequeue();
                if (message.Contains("Tx"))
                    DoGUIEscribirReceiveSendData(message, LogType.TX);
                if (message.Contains("Rx"))
                    DoGUIEscribirReceiveSendData(message, LogType.RX);

            }
            numBuffer_Recv = 0;
            numBuffer_Send = 0;
            DoGUIUpdateCommunication();
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            numFrame = 0;

            DoGUILimpiarLog();
            DoGUIUpdateCommunication();
        }

        private void btnClearBuffer_Click(object sender, RoutedEventArgs e)
        {
            numFrame = 0;
            numBuffer_Recv = 0;
            numBuffer_Send = 0;
            bMessage._queue.Clear();
            DoGUILimpiarLog();
            DoGUIUpdateCommunication();
        }

        private void AgregarLogRXTX(string mensaje, LogType logType)
        {
            String tmpStr = "";
            ListBoxItem lbItem = new ListBoxItem();
            lbItem.MouseDoubleClick += LbItem_MouseDoubleClick;
            switch (logType)
            {
                case LogType.RX:
                    lbItem.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)255, (byte)0, (byte)0));//Brushes.DarkOrange;
                    //lbItem.BorderBrush = Brushes.DarkOrange;
                    //lbItem.Background = Brushes.Red;
                    lbItem.Content = mensaje;
                    break;
                case LogType.TX:
                    lbItem.Foreground = new SolidColorBrush(Color.FromArgb(255, (byte)66, (byte)206, (byte)255)); //Brushes.LimeGreen;
                    //lbItem.BorderBrush = Brushes.LimeGreen;
                    //lbItem.Background = Brushes.Blue;
                    lbItem.Content = mensaje;
                    break;
            }
            // Insertamos en primera linea
            this.lbLog.Items.Insert(0, lbItem);

            if (this.lbLog.Items.Count > 500)
            {
                lbLog.Items.RemoveAt(500);
            }
        }

        private void LimpiarLog()
        {
            // Insertamos en primera linea
            this.lbLog.Items.Clear();
        }

        private void LbItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            string comFullData = ((ListBoxItem)sender).Content.ToString();
            //gCommData.Content = comFullData;

            int index1 = comFullData.IndexOf("=> ", StringComparison.Ordinal);
            int index2 = comFullData.IndexOf("<= ", StringComparison.Ordinal);
            int index = Math.Max(index1, index2);   //Uno de ellos siempre va ser -1

            string comData = comFullData.Substring(index + 3).Replace(" ", "").Replace("-", "");
            gCommData.Content = comData;

            byte[] toBytes = StringToByteArray(comData);

            cvComData.Children.Clear();
            cvComData.Width = (cantWidthDescription + cantWidthData) * 25 + 150;

            //TODO Cambiar a colores fijo segun aplicacion
            var brusDataLinkH = new SolidColorBrush(Color.FromArgb(255, (byte)153, (byte)255, (byte)204));       //Brush b1 = Brushes.Red;   //var brush = new SolidColorBrush(Color.FromArgb(255, (byte)R, (byte)G, (byte)B));
            var brusTransportSegment = new SolidColorBrush(Color.FromArgb(255, (byte)250, (byte)188, (byte)40));       //Brush b1 = Brushes.Red;   //var brush = new SolidColorBrush(Color.FromArgb(255, (byte)R, (byte)G, (byte)B));
            var brusBytesCounter = new SolidColorBrush(Color.FromArgb(255, (byte)200, (byte)200, (byte)200));       //Brush b1 = Brushes.Red;   //var brush = new SolidColorBrush(Color.FromArgb(255, (byte)R, (byte)G, (byte)B));
            var brush3 = Application.Current.Resources["SecundaryBackgroundColor"];
            var brush4 = Application.Current.Resources["TertiaryBackgroundColor"];
            var brush5 = Application.Current.Resources["QuarteryBackgroundColor"];
            var PrimaryTextColor = Application.Current.Resources["PrimaryTextColor"];

            for (int i = 0; i < toBytes.Count(); i++)
            {
                if (i < 10)
                {
                    drawBoderInCanvas(i * 25 + 50, 10, (Brush)brusDataLinkH);
                    drawLabelInCanvas(toBytes[i], "X2", i * 25 + 50, 10, Brushes.Black);
                    drawLabelInCanvas(i, "00", i * 25 + 50, 30, (Brush)PrimaryTextColor, 9);


                }
                else if (i >= 10)
                {
                    int _row = (i - 10) / 18;
                    int _coL = (i - 10) % 18;

                    int setLeft = _coL * 25 + 50;
                    int setTop = _row * 40 + 50;
                    drawBoderInCanvas(setLeft, setTop, (Brush)brusTransportSegment);
                    drawLabelInCanvas(toBytes[i], "X2", setLeft, setTop, Brushes.Black);
                    drawLabelInCanvas(i, "00", setLeft, setTop + 20, (Brush)PrimaryTextColor, 9);

                }
            }

            int countDataLinkLayer = 9;    //TODO DEBE CACLCULAR
            drawDataLinkLayer(brusDataLinkH, countDataLinkLayer);
            drawDataLinkLayerHeader(brusDataLinkH, 6);

            //DATA LINK LAYER
            DNP3Message dnp3Message = new DNP3Message(toBytes);
            drawBoderInCanvas(100, starY, (Brush)brusBytesCounter, cantWidthData * 25, 6 * 25, 0, 0);
            drawBoderInCanvas(122, starY, (Brush)brusDataLinkH, cantWidthData * 25, 6 * 25, 0, 0);
            drawBoderInCanvas(120 + cantWidthData * 25, starY, (Brush)brusBytesCounter, cantWidthDescription * 25, 6 * 25, 0, 0);

            //START
            drawLabelTextInCanvas("00", 100, starY, Brushes.Gray);
            drawLabelTextInCanvas(BitConverter.ToString(dnp3Message.StartMessage).Replace("-", " "), 120, starY, Brushes.Black);
            drawLabelTextInCanvas("START", 120 + cantWidthData * 25, starY, Brushes.Black);
            //LN
            drawLabelTextInCanvas("02", 100, starY + 25, Brushes.Gray);
            drawLabelTextInCanvas(dnp3Message.LN.ToString("X2"), 120, starY + 25, Brushes.Black);
            drawLabelTextInCanvas("LENGTH FIELD: " + dnp3Message.LN, 120 + cantWidthData * 25, starY + 25, Brushes.Black);
            //DLC
            drawLabelTextInCanvas("03", 100, starY + 50, Brushes.Gray);
            drawLabelTextInCanvas(dnp3Message.DLC.ToString("X2"), 120, starY + 50, Brushes.Black);
            drawLabelTextInCanvas("DATA LINK CONTROL: " + dnp3Message.DLCString(), 120 + cantWidthData * 25, starY + 50, Brushes.Black);
            //DESTINATION FIELD
            drawLabelTextInCanvas("04", 100, starY + 75, Brushes.Gray);
            drawLabelTextInCanvas(BitConverter.ToString(dnp3Message.DestField).Replace("-", " "), 120, starY + 75, Brushes.Black);
            drawLabelTextInCanvas("DESTINATION FIELD: " + (dnp3Message.DestField[0] + dnp3Message.DestField[1] * 256), 120 + cantWidthData * 25, starY + 75, Brushes.Black);
            //SOURCE FIELD
            drawLabelTextInCanvas("06", 100, starY + 100, Brushes.Gray);
            drawLabelTextInCanvas(BitConverter.ToString(dnp3Message.SourField).Replace("-", " "), 120, starY + 100, Brushes.Black);
            drawLabelTextInCanvas("SOURCE FIELD: " + (dnp3Message.SourField[0] + dnp3Message.SourField[1] * 256), 120 + cantWidthData * 25, starY + 100, Brushes.Black);
            //CRC
            drawLabelTextInCanvas("08", 100, starY + 125, Brushes.Gray);
            drawLabelTextInCanvas(BitConverter.ToString(dnp3Message.CRC).Replace("-", " "), 120, starY + 125, Brushes.Black);
            drawLabelTextInCanvas("CRC: " + (dnp3Message.CRCok ? "🗸" : "x"), 120 + cantWidthData * 25, starY + 125, Brushes.Black);
            //drawTextBlockInCanvas(BitConverter.ToString(dnp3Message.StartMessage).Replace("-"," "), 100, starY, cantWidthData*30, heightData, Brushes.Gray);


        }

        int cantWidthDescription = 50;
        int cantWidthData = 8;
        int heightData = 25;
        int starY = 100; //TODO depende de cuantas lineas se generen

        public void drawDataLinkLayer(Brush brushColor, int countDataLinkLayer) //Frames
        {
            drawTextBlockInCanvas("DATA LINK LAYER", 50, countDataLinkLayer * heightData + starY-1, countDataLinkLayer * heightData-1, 18, brushColor, true, 3 );
        }

        public void drawDataLinkLayerHeader(Brush brushColor, int countDataLinkLayer)
        {
            drawTextBlockInCanvas("DATA LINK HEADER", 70, countDataLinkLayer * heightData + starY-1, countDataLinkLayer * heightData-1, 30, brushColor, true, 10);
        }

        public void drawTransportLayer()    //Segments
        { }

        public void drawTransportLayerHeader()
        { }
        public void drawApplicationLayer() //Fragments
        { }
        public void drawApplicationLayerHeader()
        { }


        public void drawBoderInCanvas(double setLeft, double setTop, Brush brushColor, double Width = 25, double Height = 25, int cornerRadius = 3, int borderThickness = 1)
        {
            Border border = new Border { Width = Width, Height = Height, CornerRadius = new CornerRadius(cornerRadius), BorderThickness = new Thickness(1), Background = brushColor };
            Canvas.SetLeft(border, setLeft);
            Canvas.SetTop(border, setTop);
            cvComData.Children.Add(border);
        }

        public void drawLabelInCanvas(int intValue, string format, double setLeft, double setTop, Brush Foreground, double FontSize = 12)
        {
            Label label = new Label { Content = intValue.ToString(format), FontWeight = FontWeights.Normal, FontSize = FontSize, Foreground = Foreground };   //X: hexadecimal pero puede poner solo 1 digital
            Canvas.SetLeft(label, setLeft);
            Canvas.SetTop(label, setTop);
            cvComData.Children.Add(label);
        }

        public void drawLabelTextInCanvas(string textValue, double setLeft, double setTop, Brush Foreground, double FontSize = 12)
        {
            Label label = new Label { Content = textValue, FontWeight = FontWeights.Normal, FontSize = FontSize, Foreground = Foreground };   //X: hexadecimal pero puede poner solo 1 digital
            Canvas.SetLeft(label, setLeft);
            Canvas.SetTop(label, setTop);
            cvComData.Children.Add(label);
        }

        public void drawTextBlockInCanvas(string textValue, double setLeft, double setTop, double Width, double Height, Brush Background, bool Style = false, int padding = 0)
        {
            TextBlock textBlock = new TextBlock
            {
                Text = textValue,
                Width = Width,
                Height = Height,
                Background = Background,
                FontFamily = new FontFamily("Courier New"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                Padding = new Thickness(padding),
            };
            if (Style)
            {
                textBlock.Style = (Style)Application.Current.Resources["verticalText"];
            }
            Canvas.SetLeft(textBlock, setLeft);
            Canvas.SetTop(textBlock, setTop);
            cvComData.Children.Add(textBlock);
        }

        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();

        }

        private void UpdateCountBuffer()
        {
            DataBuffer.Text = bMessage._queue.Count.ToString();
            DataBufferPorcentaje.Text = ((float)(bMessage._queue.Count) / (float)(bMessage._maxSize) * 100).ToString("0.0") + " % ";
            DataBufferRecv.Text = numBuffer_Recv.ToString();
            DataBufferSend.Text = numBuffer_Send.ToString();
            DataFrame.Text = numFrame.ToString();
        }

        #endregion


    }
}