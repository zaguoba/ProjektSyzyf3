using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Diagnostics;
using System.Threading.Tasks;

using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Devices;
using Windows.Devices.Gpio;
using Windows.Devices.Spi;
using Windows.Devices.Enumeration;

using Microsoft.IoT.Lightning.Providers;

namespace projektSyzyf3
{
    // contains definitions of custom gpio names for serial communication with ADC and init method
    public class SimpleSerialProtocol
    {
        //GENERAL
        public GpioPin testPin; //BCM19
        public GpioPin _PD;     //BCM26
        //public GpioPin CLK192; //future clock source derived from RPi to drive ADC at full speed

        ////CHANNEL1
        //public GpioPin CLK1;    //BCM4
        //public GpioPin FSO1;    //BCM25
        //public GpioPin DOUT1;   //BCM6
        //public GpioPin SCLK1;   //BCM13
        //public GpioPin OTR1;    //BCM23
        //public GpioPin SYNC1;   //BCM18

        //CHANNEL2
        public GpioPin CLK2;    //BCM27
        public GpioPin FSO2;    //BCM24
        public GpioPin DOUT2;   //BCM5
        public GpioPin SCLK2;   //BCM12
        public GpioPin OTR2;    //BCM22
        public GpioPin SYNC2;   //BCM17

        //bit-bang SPI
        public GpioPin _CS0;    //BCM8
        public GpioPin aSCLK;    //BCM11
        public GpioPin DIO;     //BCM10  

        //AUX
        //public byte initProgress = 0;
        private const byte F_ = 0b11110000;
        private const byte _R = 0b00001111;
        private byte CLKedge = 0; 
        private byte SCLKedge = 0;
        private byte FSOedge = 0;

        //STOPWATCH
        public long StoperOne = 0;
        public long[] StoperArr = new long[22];
        public uint StoperCnt = 0;

        public SimpleSerialProtocol()
        {

        }

        public void InitGPIO()
        {
            if (LightningProvider.IsLightningEnabled)
            {
                LowLevelDevicesController.DefaultProvider = LightningProvider.GetAggregateProvider();
            }

            // Get the default GPIO controller on the system
            GpioController gpio = GpioController.GetDefault();
            if (gpio == null)
                return; // GPIO not available on this system

            // Open GPIO 25
            testPin = gpio.OpenPin(19);
            // Latch HIGH value first. This ensures a default value when the pin is set as output
            testPin.Write(GpioPinValue.High);
            // Set the IO direction as output
            testPin.SetDriveMode(GpioPinDriveMode.Output);

            // Open GPIO 27 - CLK listener
            CLK2 = gpio.OpenPin(27);
            CLK2.SetDriveMode(GpioPinDriveMode.Input);

            // Open GPIO 24 - FSO input
            FSO2 = gpio.OpenPin(24);
            FSO2.SetDriveMode(GpioPinDriveMode.InputPullDown);

            // Open GPIO 5 - DOUT input
            DOUT2 = gpio.OpenPin(5);
            DOUT2.SetDriveMode(GpioPinDriveMode.InputPullDown);

            // Open GPIO 12 - SCLK input
            SCLK2 = gpio.OpenPin(12);
            SCLK2.SetDriveMode(GpioPinDriveMode.InputPullDown);

            // Open GPIO 22 - OTR input
            OTR2 = gpio.OpenPin(22);
            OTR2.SetDriveMode(GpioPinDriveMode.InputPullDown);

            // Open GPIO 17 - issue SYNC pulse to ADC
            SYNC2 = gpio.OpenPin(17);
            SYNC2.Write(GpioPinValue.Low);
            SYNC2.SetDriveMode(GpioPinDriveMode.Output);

            //Set PowerDown pin high to enable opamp and ADC
            _PD = gpio.OpenPin(26);
            _PD.Write(GpioPinValue.High);
            _PD.SetDriveMode(GpioPinDriveMode.Output);

            //// Open GPIO 8 - issue _CS0 pulse to PGA
            //_CS0 = gpio.OpenPin(8);
            //_CS0.Write(GpioPinValue.High);
            //_CS0.SetDriveMode(GpioPinDriveMode.Output);

            //// Open GPIO 11 - issue SCLK pulse to PGA
            //aSCLK = gpio.OpenPin(11);
            //aSCLK.Write(GpioPinValue.Low);
            //aSCLK.SetDriveMode(GpioPinDriveMode.Output);

            //// Open GPIO 10 - master DIO communication line to PGA
            //DIO = gpio.OpenPin(10);
            //DIO.Write(GpioPinValue.Low);
            //DIO.SetDriveMode(GpioPinDriveMode.Output);
        }

        public byte InitConversion()
        {
            //CLK2.ValueChanged += EdgeDetCLKOnValueChanged; //listen to edges on CLK

            byte initProgress = 0;

            //long startInitProcess = 0, elapsedInitProcess = 0;

            //GCLatencyMode oldMode = GCSettings.LatencyMode;

            //RuntimeHelpers.PrepareConstrainedRegions();

            //try
            //{
            //    GCSettings.LatencyMode = GCLatencyMode.LowLatency;

            //    CLK2.ValueChanged += EdgeDetCLKOnValueChanged; //listen to edges on CLK

            while (initProgress != 0b11111111)
            {
                if (CLK2.Read().Equals(GpioPinValue.High) && initProgress == 0)
                {
                    initProgress |= 0b00000001;
                }
                if (CLK2.Read().Equals(GpioPinValue.Low) && initProgress == 1)
                {
                    SYNC2.Write(GpioPinValue.High);
                    initProgress |= 0b00000010;
                }
                if (CLK2.Read().Equals(GpioPinValue.High) && initProgress == 3)
                {
                    initProgress |= 0b00000100;
                }
                if (CLK2.Read().Equals(GpioPinValue.Low) && initProgress == 7)
                {
                    SYNC2.Write(GpioPinValue.Low);
                    initProgress |= 0b00001000;
                }
                if (FSO2.Read().Equals(GpioPinValue.High) && initProgress == 15)
                {
                    initProgress |= 0b00010000;
                }
                if (FSO2.Read().Equals(GpioPinValue.Low) && initProgress == 15)
                {
                    return initProgress = 0; //no response from ADC
                }
                if (FSO2.Read().Equals(GpioPinValue.Low) && initProgress == 31)
                {
                    initProgress |= 0b00100000;
                }
                if (FSO2.Read().Equals(GpioPinValue.High) && initProgress == 63)
                {
                    initProgress |= 0b01000000;
                }
                if (FSO2.Read().Equals(GpioPinValue.Low) && initProgress == 127)
                {
                    initProgress |= 0b10000000;
                }
            }





            //    if (CLKedge == F_ && initProgress == 0)
            //        {
            //            CLKedge = 0;
            //            SYNC2.Write(GpioPinValue.High);
            //            //startInitProcess = Stopwatch.GetTimestamp();
            //            initProgress |= 0b00000001;
            //        }
            //        if (CLKedge == F_ && initProgress == 1)
            //        {
            //            CLKedge = 0;
            //            SYNC2.Write(GpioPinValue.Low);
            //            //elapsedInitProcess = Stopwatch.GetTimestamp() - startInitProcess;
            //            initProgress |= 0b00000010;
            //        }
            //        if (FSO2.Read().Equals(GpioPinValue.Low) && initProgress == 3)
            //            initProgress = 0; //startOver
            //        //    return elapsedInitProcess; //init failed
            //        if (FSO2.Read().Equals(GpioPinValue.High) && initProgress == 3)
            //        {
            //            FSO2.ValueChanged += EdgeDetFSOOnValueChanged; //listen to edges on FSO
            //            initProgress |= 0b00001100;
            //        }
            //        if (FSOedge == F_ && initProgress == 15)
            //        {
            //            FSOedge = 0;
            //            initProgress |= 0b00110000;
            //        }
            //        if (FSOedge == _R && initProgress == 63)
            //        {
            //            FSOedge = 0;
            //            initProgress |= 0b11000000;
            //        }

            //        //escape while if init is taking too long
            //        if (CLKedge == _R)
            //        {
            //            CLKedge = 0;
            //            initTimeout++;
            //        }
            //        if (initTimeout >= 1000)
            //            return initProgress;
            //    }

            //    CLK2.ValueChanged -= EdgeDetCLKOnValueChanged; //cease to listen CLK
            //}
            //finally
            //{
            //    // ALWAYS set the latency mode back
            //    GCSettings.LatencyMode = oldMode;
            //}

            return initProgress;

        }

        public short CollectFrame()
        {
            ushort theFrame = 0;
            ushort collectProgress = 0x8002; // 0x8001;
            SCLKedge = 1; 
            FSOedge = 0;

            while (collectProgress != 0)
            {
                if (collectProgress == 0x8002 && FSO2.Read().Equals(GpioPinValue.High))
                {
                    collectProgress = 0x8001;
                }
                if (collectProgress == 0x8001 && FSO2.Read().Equals(GpioPinValue.Low))
                {
                    collectProgress = 0x8000;
                }
                if (0 < collectProgress && collectProgress <= 0x8000)
                {
                    if (SCLK2.Read().Equals(GpioPinValue.Low) && SCLKedge == 1)
                    {
                        SCLKedge = 0; //falling SCLK edge
                        if (DOUT2.Read().Equals(GpioPinValue.High))
                        {
                            theFrame |= collectProgress; //if data line is high, set current bit high
                        }
                        collectProgress >>= 1; //shift to next bit (less significant)
                    }
                    if (SCLK2.Read().Equals(GpioPinValue.High) && SCLKedge == 0)
                    {
                        SCLKedge = 1; //rising SCLK edge
                    }
                }



                //if (FSOedge == F_ && collectProgress == 0x8001)
                //{
                //    FSOedge = 0; //restore edgeDet marker
                //    collectProgress = 0x8000;
                //    SCLK2.ValueChanged += EdgeDetSCLKOnValueChanged; //listen to edges on SCLK
                //    SCLKedge = 0;
                //}
                //if (SCLKedge == F_ && collectProgress < 0x8001)
                //{
                //    SCLKedge = 0;
                //    if (DOUT2.Read().Equals(GpioPinValue.High))
                //    {
                //        theFrame |= collectProgress; //if data line is high, set current bit high
                //    }
                //    collectProgress >>= 1; //shift to next bit (less significant)
                //}

            }

            //SCLK2.ValueChanged -= EdgeDetSCLKOnValueChanged; 
            return (short)theFrame;
        }

        public float toVolts(short theFrame)
        {
            int intFrame = theFrame + 31440;

            return ((float)intFrame * 3.2f) / 65536.0f;
        }

        public void HoldConversion()
        {
            _PD.Write(GpioPinValue.Low);
        }

        //private void EdgeDetCLKOnValueChanged(GpioPin sender, GpioPinValueChangedEventArgs args)
        //{
        //    if (args.Edge.Equals(GpioPinEdge.FallingEdge))
        //    {
        //        if(initProgress == 0b00000000)
        //        {
        //            SYNC2.Write(GpioPinValue.High);
        //            initProgress |= 0b00000011;
        //        }
        //        else if (initProgress == 0b00000011)
        //        {
        //            SYNC2.Write(GpioPinValue.Low);
        //            if (FSO2.Read().Equals(GpioPinValue.Low))
        //                initProgress = 0; //startOver
        //            if (FSO2.Read().Equals(GpioPinValue.High))
        //            {
        //                FSO2.ValueChanged += EdgeDetFSOOnValueChanged; //listen to edges on FSO
        //                initProgress |= 0b00001100;
        //            }
        //        }
        //    }
        //}

        //private void EdgeDetFSOOnValueChanged(GpioPin sender, GpioPinValueChangedEventArgs args)
        //{
        //    if (args.Edge.Equals(GpioPinEdge.FallingEdge))
        //    {
        //        if (initProgress == 0b00001111)
        //            initProgress |= 0b00110000;
        //    }
        //    if (args.Edge.Equals(GpioPinEdge.RisingEdge))
        //    {
        //        if (initProgress == 0b00111111)
        //        {

        //        }
        //    }
        //}

        //private void EdgeDetSCLKOnValueChanged(GpioPin sender, GpioPinValueChangedEventArgs args)
        //{
        //    if (args.Edge.Equals(GpioPinEdge.FallingEdge))
        //    {
        //        SCLKedge = F_;

        //        //if (StoperCnt < 20)
        //        //{
        //        //    StoperArr[StoperCnt] = Stopwatch.GetTimestamp();
        //        //    StoperCnt++;

        //        //}
        //    }
        //    if (args.Edge.Equals(GpioPinEdge.RisingEdge))
        //        SCLKedge = _R;
        //}





    }


    public static class PGA113SPI
    {
        public const byte readCmd = 0b01101010;
        public const byte writeCmd = 0b00101010;

        public static async Task<SpiDevice> InitSPI0(int chipSelect)
        {
            var spi0Aqs = SpiDevice.GetDeviceSelector("SPI0");
            var devicesInfo = await DeviceInformation.FindAllAsync(spi0Aqs);
            var settings = new SpiConnectionSettings(chipSelect)
            {
                ClockFrequency = 100000,
                DataBitLength = 8,
                Mode = SpiMode.Mode0,
                SharingMode = SpiSharingMode.Shared
            };

            //SpiController controller = await SpiController.GetDefaultAsync();

            return await SpiDevice.FromIdAsync(devicesInfo[0].Id, settings);
            //return controller.GetDevice(settings);
        }
    }

    public class NullableBooleanToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool?)
            {
                return (bool)value;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is bool)
                return (bool)value;
            return false;
        }
    }

    /// <summary>
    /// Zapewnia zachowanie specyficzne dla aplikacji, aby uzupełnić domyślną klasę aplikacji.
    /// </summary>
    sealed partial class App : Application
    {
        /// <summary>
        /// Inicjuje pojedynczy obiekt aplikacji. Jest to pierwszy wiersz napisanego kodu
        /// wykonywanego i jest logicznym odpowiednikiem metod main() lub WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
        }

        /// <summary>
        /// Wywoływane, gdy aplikacja jest uruchamiana normalnie przez użytkownika końcowego. Inne punkty wejścia
        /// będą używane, kiedy aplikacja zostanie uruchomiona w celu otworzenia określonego pliku.
        /// </summary>
        /// <param name="e">Szczegóły dotyczące żądania uruchomienia i procesu.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;

            // Nie powtarzaj inicjowania aplikacji, gdy w oknie znajduje się już zawartość,
            // upewnij się tylko, że okno jest aktywne
            if (rootFrame == null)
            {
                // Utwórz ramkę, która będzie pełnić funkcję kontekstu nawigacji, i przejdź do pierwszej strony
                rootFrame = new Frame();

                rootFrame.NavigationFailed += OnNavigationFailed;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    //TODO: Załaduj stan z wstrzymanej wcześniej aplikacji
                }

                // Umieść ramkę w bieżącym oknie
                Window.Current.Content = rootFrame;
            }

            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    // Kiedy stos nawigacji nie jest przywrócony, przejdź do pierwszej strony,
                    // konfigurując nową stronę przez przekazanie wymaganych informacji jako
                    // parametr
                    rootFrame.Navigate(typeof(MainPage), e.Arguments);
                }
                // Upewnij się, ze bieżące okno jest aktywne
                Window.Current.Activate();
            }
        }

        /// <summary>
        /// Wywoływane, gdy nawigacja do konkretnej strony nie powiedzie się
        /// </summary>
        /// <param name="sender">Ramka, do której nawigacja nie powiodła się</param>
        /// <param name="e">Szczegóły dotyczące niepowodzenia nawigacji</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        /// <summary>
        /// Wywoływane, gdy wykonanie aplikacji jest wstrzymywane. Stan aplikacji jest zapisywany
        /// bez wiedzy o tym, czy aplikacja zostanie zakończona, czy wznowiona z niezmienioną zawartością
        /// pamięci.
        /// </summary>
        /// <param name="sender">Źródło żądania wstrzymania.</param>
        /// <param name="e">Szczegóły żądania wstrzymania.</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            //TODO: Zapisz stan aplikacji i zatrzymaj wszelkie aktywności w tle
            deferral.Complete();
        }
    }
}
