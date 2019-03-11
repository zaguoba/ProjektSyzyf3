using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Globalization;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Devices.Gpio;
using Windows.Devices.Spi;
using Windows.Devices.Enumeration;
using Windows.Storage;



namespace projektSyzyf3
{
    public sealed partial class MainPage : Page
    {
        SimpleSerialProtocol SSP = new SimpleSerialProtocol();

        StorageFile resultsCSV;

        SpiDevice PGA113top, PGA113btm;
        public const byte readCmd = 0b01101010;
        public const ushort writeCmd = 0b00101010;

        int click = 0;
        Int64 starttime = 0, stoptime = 0;

        public MainPage()
        {
            SSP.InitGPIO();

            

            this.InitializeComponent();
            gainCombo.SelectionChanged += PGAcombos_SelectionChanged;
            channelCombo.SelectionChanged += PGAcombos_SelectionChanged;
        }

        private async void connect_Click(object sender, RoutedEventArgs e)
        {
            connect.Content = "Łączę...";

            StorageFolder dokuMenty = KnownFolders.DocumentsLibrary;
            resultsCSV = await dokuMenty.CreateFileAsync("results.csv", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(resultsCSV, "Timestamp;Value\n");

            if (PGA113btm == null)
                PGA113btm = await PGA113SPI.InitSPI0(0);
            PGA113btm.Write(new byte[] { PGA113SPI.writeCmd, 0b00000001 }); //init Gain=1 & VCAL/CH0
            results.Text = Stopwatch.Frequency.ToString() + "\n\n"; // " ";

            byte initResult = SSP.InitConversion();

            if (initResult == 0b11111111)
            {
                connect.Content = "Połączono\nz ADC";
                connect.Background = new SolidColorBrush(Color.FromArgb(255, 79, 234, 164));
                funButton.IsEnabled = true;
                nSamples.IsEnabled = true;

            }
            else
                results.Text = "sromotna porażka";
        }

        private async void PGAcombos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem tempGain = (ComboBoxItem)gainCombo.SelectedItem;
            ComboBoxItem tempChan = (ComboBoxItem)channelCombo.SelectedItem;
            byte spiConfigCmd = Convert.ToByte(tempGain.Tag);
            spiConfigCmd |= Convert.ToByte(tempChan.Tag);
            ushort bbSpiCmd = writeCmd;
            bbSpiCmd |= spiConfigCmd;

            results.Text += '\n' + tempGain.Tag.ToString() + ' ' + tempChan.Tag.ToString();
            results.Text += ", binary: 0b" + Convert.ToString(spiConfigCmd, 2).PadLeft(8, '0') + '\n';

            if(PGA113btm == null)
                PGA113btm = await PGA113SPI.InitSPI0(0);
            PGA113btm.Write(new byte[] { PGA113SPI.writeCmd, spiConfigCmd });
        }

        private void clrButton_Click(object sender, RoutedEventArgs e)
        {
            results.Text = "";//CultureInfo.CurrentCulture.ToString();
        }

        private void PasekPostepu_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            progText.Text = pasekPostepu.Value.ToString();
        }

        private void NSamples_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter && funButton.IsEnabled)
            {
                funButton_Click(this, new RoutedEventArgs());
            }
        }

        private async void funButton_Click(object sender, RoutedEventArgs e)
        {
            int nSamplesInt = 1, refined = 0, consec = 0;
            Int32.TryParse(nSamples.Text, out nSamplesInt);

            if (nSamplesInt > 1)
            {
                funButton.IsEnabled = false;
                var messBox = Flyout.GetAttachedFlyout(results);

                //setup error removal
                double initfilterSpike = 0.0;
                if (refineBox.IsChecked.Value)
                {
                    ComboBoxItem tempRefine = (ComboBoxItem)refineByMe.SelectedItem;
                    initfilterSpike = Convert.ToDouble(tempRefine.Content);
                }

                //setup progressbar
                pasekPostepu.Value = 0;

                //lambda expression - reporting progress from Task
                var progres = new Progress<int>(postep =>
                {
                    pasekPostepu.Value = postep;
                    if (pasekPostepu.Value >= 99)
                        messBox.ShowAt(results);


                });

                string tempbuffer = await Task.Run(() => CollectMany(nSamplesInt, initfilterSpike, progres, ref refined, ref consec));
                await FileIO.WriteTextAsync(resultsCSV, tempbuffer);
                tempbuffer = null;

                results.Text += "Zapisano " + nSamplesInt + " próbek do pliku results.csv\nRefined "
                    + refined + " samples with " + initfilterSpike + " filter\nConsec: " + consec + "\n";

                messBox.Hide();
                funButton.IsEnabled = true;
            }
            else
            {
                short theFrame = SSP.CollectFrame();

                //while (SSP.StoperCnt < 20) ;
                //for (int i = 0; i < 19; i++)
                //{
                //    obliczonka = (SSP.StoperArr[i + 1] - SSP.StoperArr[i]) * (1000.0 / Stopwatch.Frequency);
                //    results.Text += obliczonka.ToString() + " ms\n";
                //}

                results.Text += "Obtained value: " + SSP.toVolts(theFrame).ToString("F") +
                                " V, decimal " + theFrame.ToString() +
                                ", binary: 0b" + Convert.ToString(theFrame, 2).PadLeft(16, '0') + "\n";
            }
        }

        private string CollectMany(int howMany, double initfilterSpike, IProgress<int> progres, ref int refined, ref int consec)
        {
            long timValStart;
            int progressFactor = howMany / 100;
            double[,] result = new double[howMany, 4];
            StringBuilder buffer = new StringBuilder(70 * howMany);
                
            buffer.Append("MTime; MDur; intValue; Volts\n");


            timValStart = Stopwatch.GetTimestamp();


            for (int i = 0; i < howMany; i++)
            {
                result[i, 0] = Stopwatch.GetTimestamp(); //timValConv
                result[i, 2] = SSP.CollectFrame(); //result
                result[i, 1] = Stopwatch.GetTimestamp(); //timValDur

                //buffer += secElapsed + "; " + msecDur + "; " + result + "\n";

                if(i % progressFactor == 0)
                {
                    progres.Report(i / progressFactor);
                }
            }

            for (int i = 0; i < howMany; i++)
            {
                //result[i, 2] += 29325.0;
                //result[i, 2] /= 11628.0;
                result[i, 3] = SSP.toVolts((short)result[i, 2]);
                result[i, 1] = (result[i, 1] - result[i, 0]) * (1.0 / Stopwatch.Frequency); //msecDur
                result[i, 0] = (result[i, 0] - timValStart) * (1.0 / Stopwatch.Frequency); //secElapsed
                //buffer += result[i, 0] + "; " + result[i, 1] + "; " + result[i, 2] + "\n";

            }

            if (initfilterSpike > 0) //spike removal filter
            {
                for (int i = 0; i < howMany - 2; i++)
                {
                    int target = 0;
                    double filterSpike = initfilterSpike;

                    if (Math.Abs(result[i, 2] - result[i + 1, 2]) > filterSpike)
                    {
                        for (int s = 2; s < 5 && i + s < howMany; s++) //check next 3 samples for match 
                        {
                            if (Math.Abs(result[i, 2] - result[i + s, 2]) < filterSpike || i + s == howMany - 1) //or if end of sample table, quit
                            {
                                target = s; //if found, save target index and quit loop
                                break;
                            }
                            filterSpike *= 1.1;
                        }
                        double refineSpikeAv = (result[i, 2] + result[i + target, 2]) / 2; //average two correct values surrounding spike
                        for (int t = 1; t < target; t++) //cycle through corrupted spikes and assign them average value of adjacent correct ones
                        {
                            result[i + t, 2] = refineSpikeAv;
                            refined++;
                            if (t > 1)
                                consec++;
                        }
                    }
                }
            }

            for (int i = 0; i < howMany; i++)
                buffer.Append(result[i, 0] + "; " + result[i, 1] + "; " + result[i, 2] + "; " + result[i, 3] + "\n");

            return buffer.ToString();
        }
    }
}
