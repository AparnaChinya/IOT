using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.Core;
using Windows.Devices.Gpio;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace RaspberryDemo
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private StreamSocket clientSocket;
        private HostName serverHost;
        private string serverPort = "2212";
        private bool connected = false;
        private bool closing = false;

        public string hostToConnect = "10.171.71.71";
        StreamSocket socket;
        StreamSocketListener listener;

        private const int LED_PIN = 27;
        private GpioPin myPin;
        private GpioPinValue pinValue;
        private bool bGpioStatus = false;

        string sendString = "";
        DataReader reader;

        public MainPage()
        {
            this.InitializeComponent();

            CheckDeviceType();


        }


        


        protected override void OnNavigatedTo(NavigationEventArgs args)
        {
            if (args.Parameter != null)
            {
                string r = args.Parameter.ToString();

                sendString = r;


            }
           
        }


    


        public void CheckDeviceType()
        {
            var api = Windows.System.Profile.AnalyticsInfo.VersionInfo.DeviceFamily;
            if (api == "Windows.Desktop" || api == "Windows.Mobile")
            {
                //SendButton.Visibility = Visibility.Visible;
                ConnectButton.Visibility = Visibility.Visible;
                //stringToSend.Visibility = Visibility.Visible;
                IPAddress.Visibility = Visibility.Visible;
                //hostToConnect = IPAddress.Text;
                Connect();
            }

            if (api == "Windows.IoT")
            {

                //ListenButton.Visibility = Visibility.Visible;

                Listen();
                InitGPIO();




            }



        }

        private void InitGPIO()
        {
            SendMessage.Text = "GPIO Init";
            var gpio = GpioController.GetDefault();

            SendMessage.Text = "GPIO is " + gpio.ToString();

            if (gpio == null)
            {

                myPin = null;
                bGpioStatus = false;
                SendMessage.Text = "There is no GPIO controller on this device.";
                return;

            }

            
            GpioOpenStatus openStatus = new GpioOpenStatus();
            gpio.TryOpenPin(27, GpioSharingMode.Exclusive, out myPin, out openStatus);
          

            if (myPin == null)
            {
                bGpioStatus = false;
                SendMessage.Text = "There were problems initializing the GPIO pin.";
                return;
            }

            myPin.Write(GpioPinValue.High);
            myPin.SetDriveMode(GpioPinDriveMode.Output);

            pinValue = GpioPinValue.Low;
            myPin.Write(pinValue);

            SendMessage.Text = "GPIO pin initialized correctly.";
            bGpioStatus = true;
        }

        public async void Listen()
        {
            ReceiveData.Text = "Listening...";

            if (CoreApplication.Properties.ContainsKey("listener"))
            {
                SendMessage.Text = "This step has already been executed. Please move to the next one.";
                return;
            }

            if (String.IsNullOrEmpty(serverPort))
            {
                SendMessage.Text = "Please provide a service name.";
                return;
            }

            CoreApplication.Properties.Remove("serverAddress");


            CoreApplication.Properties.Add("serverAddress", serverHost);


            listener = new StreamSocketListener();
            listener.ConnectionReceived += OnConnection;
            listener.Control.KeepAlive = false;

            CoreApplication.Properties.Add("listener", listener);

            await listener.BindServiceNameAsync(serverPort);


        }

        private void ListenButton_Click(object sender, RoutedEventArgs e)
        {

             Listen();


        }

        private async void OnConnection(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
        {
            try
            {
                while (true)
                {
                    reader = new DataReader(args.Socket.InputStream);
                    // Read first 4 bytes (length of the subsequent string).
                    uint sizeFieldCount = await reader.LoadAsync(sizeof(uint));
                    if (sizeFieldCount != sizeof(uint))
                    {
                        // The underlying socket was closed before we were able to read the whole data.
                        return;
                    }

                    // Read the string.
                    uint stringLength = reader.ReadUInt32();
                    uint actualStringLength = await reader.LoadAsync(stringLength);
                    if (stringLength != actualStringLength)
                    {
                        // The underlying socket was closed before we were able to read the whole data.
                        return;
                    }

                    string s = reader.ReadString(actualStringLength);

                   

                    // Display the string on the screen. The event is invoked on a non-UI thread, so we need to marshal
                    // the text back to the UI thread.
                    NotifyUserFromAsyncThread(
                      String.Format("Received data: \"{0}\"", s),
                      NotifyType.StatusMessage);

                    
                    bool t = s.ToLower().Contains("on");

                    bool f = s.ToLower().Contains("off");


                    if (t)
                    {
                        if (bGpioStatus)
                        {
                            pinValue = GpioPinValue.High;
                            myPin.Write(pinValue);

                            NotifyUserFromAsyncThreadBulb(1);                        


                        }
                    }
                    else
                    if(f)
                    {
                        if (bGpioStatus)
                        {
                            pinValue = GpioPinValue.Low;
                            myPin.Write(pinValue);
                            NotifyUserFromAsyncThreadBulb(0);

                        }
                    }

                   

                    //reader.DetachStream();
                }
            }
            catch (Exception exception)
            {
                // If this is an unknown status it means that the error is fatal and retry will likely fail.
                if (SocketError.GetStatus(exception.HResult) == SocketErrorStatus.Unknown)
                {
                    
                    throw;
                }

                //SendMessage.Text =
                //    "Read stream failed with error: " + exception.Message;

            }
        }

        private void CloseSocket()
        {
            object outValue;
            if (CoreApplication.Properties.TryGetValue("clientDataWriter", out outValue))
            {
                // Remove the data writer from the list of application properties as we are about to close it.
                CoreApplication.Properties.Remove("clientDataWriter");
                DataWriter dataWriter = (DataWriter)outValue;

                // To reuse the socket with another data writer, the application must detach the stream from the
                // current writer before disposing it. This is added for completeness, as this sample closes the socket
                // in the very next block.
                dataWriter.DetachStream();
                dataWriter.Dispose();
            }
            if (CoreApplication.Properties.TryGetValue("clientSocket", out outValue))
            {
                // Remove the socket from the list of application properties as we are about to close it.
                CoreApplication.Properties.Remove("clientSocket");
                StreamSocket socket = (StreamSocket)outValue;

                // StreamSocket.Close() is exposed through the Dispose() method in C#.
                // The call below explicitly closes the socket.
                socket.Dispose();
            }
        }

        //private void SendButton_Click(object sender, RoutedEventArgs e)
        //{
        //    // Send();
        //}

        public async void Send()
        {
            if (!CoreApplication.Properties.ContainsKey("connected"))
            {
                ErrorMessage.Text = "Please run previous steps before doing this one.";
                return;
            }

            object outValue;
            StreamSocket socket;
            if (!CoreApplication.Properties.TryGetValue("clientSocket", out outValue))
            {
                ErrorMessage.Text = "Please run previous steps before doing this one.";
                return;
            }

            socket = (StreamSocket)outValue;

            // Create a DataWriter if we did not create one yet. Otherwise use one that is already cached.
            DataWriter writer;
            if (!CoreApplication.Properties.TryGetValue("clientDataWriter", out outValue))
            {
                writer = new DataWriter(socket.OutputStream);
                CoreApplication.Properties.Add("clientDataWriter", writer);
            }
            else
            {
                writer = (DataWriter)outValue;
            }

            // Write first the length of the string as UINT32 value followed up by the string. 
            // Writing data to the writer will just store data in memory.
            //  string stringToSend = "Hello";
            writer.WriteUInt32(writer.MeasureString(sendString));
            writer.WriteString(sendString);

            // Write the locally buffered data to the network.
            try
            {
                await writer.StoreAsync();
               // SendMessage.Text = "\"" + stringToSend.ToString() + "\" sent successfully.";
                CloseSocket();
                // BulbFunction();
            }
            catch (Exception exception)
            {
                // If this is an unknown status it means that the error if fatal and retry will likely fail.
                if (SocketError.GetStatus(exception.HResult) == SocketErrorStatus.Unknown)
                {
                    throw;
                }

                SendMessage.Text = "Send failed with error: " + exception.Message;
            }
        }
        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            Connect();
        }

        public async void Connect()
        {
            if (CoreApplication.Properties.ContainsKey("clientSocket"))
            {
                ErrorMessage.Text =
                    "This step has already been executed. Please move to the next one.";
                return;
            }
            if (String.IsNullOrEmpty(serverPort))
            {
                ErrorMessage.Text = "Please provide a service name.";
                return;
            }

           


            try
            {

                ErrorMessage.Text = "Trying to connect ...";
               
                serverHost = new HostName(hostToConnect);


                socket = new StreamSocket();

                socket.Control.KeepAlive = false;
                CoreApplication.Properties.Add("clientSocket", socket);

                await socket.ConnectAsync(serverHost, serverPort);
                connected = true;

                ErrorMessage.Text = "Connection established" + Environment.NewLine;
                
                CoreApplication.Properties.Add("connected", null);

                if(connected)
                {
                    Send();
                }
               

            }
            catch (Exception exception)
            {

                if (SocketError.GetStatus(exception.HResult) == SocketErrorStatus.Unknown)
                {
                    throw;
                }

                ErrorMessage.Text = "Connect failed with error: " + exception.Message;

                closing = true;
                // the Close method is mapped to the C# Dispose
                socket.Dispose();
                socket = null;

            }
        }
        private void NotifyUserFromAsyncThread(string strMessage, NotifyType type)
        {
            var ignore = Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal, () => NotifyUser(strMessage, type));
        }
        private void NotifyUserFromAsyncThreadBulb(int i)
        {
            var ignore = Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal, () => BulbVisibility(i));
        }

        private void BulbVisibility(int i)
        {
            if(i==1)
            {
                BulbImageOn.Opacity = 1;
                BulbImageOff.Opacity = 0;
            }
            else
                if(i==0)
            {
                BulbImageOn.Opacity = 0;
                BulbImageOff.Opacity = 1;
            }
        }

        private void NotifyUser(string strMessage, NotifyType type)
        {
            ReceiveData.Text = strMessage;
            //BulbToggle.Visibility = Visibility.Visible;
        }

        public enum NotifyType
        {
            StatusMessage,
            ErrorMessage
        };

        private void BulbToggle_Toggled(object sender, RoutedEventArgs e)
        {
            
            if (BulbToggle.IsOn)
            {

                if (bGpioStatus)
                {
                    pinValue = GpioPinValue.High;
                    myPin.Write(pinValue);
                    SendMessage.Text = "Bulb ON";
                }

            }
            else

            {
                if (bGpioStatus)
                {
                    pinValue = GpioPinValue.Low;
                    myPin.Write(pinValue);
                    SendMessage.Text = "Bulb OFF";
                }
            }


        }

     
    }
}
