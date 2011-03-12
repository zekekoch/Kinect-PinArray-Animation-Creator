//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//
// This library is part of CL NUI SDK
// It allows the use of Microsoft Kinect cameras in your own applications
//
// For updates and file downloads go to: http://codelaboratories.com/get/kinect
//
// Copyright 2010 (c) Code Laboratories, Inc.  All rights reserved.
//
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Threading;
using System.Diagnostics;
using System.Data;
using System.IO;

namespace CLNUIDeviceTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private IntPtr motor = IntPtr.Zero;
        private IntPtr camera = IntPtr.Zero;

        private NUIImage colorImage;
        private NUIImage depthImage;
        private NUIImage pinImage;
        private NUIImage rawImage;

        private int[,] colorArray = new int[64, 48];
        private float[,] depthArray = new float[64, 48];

        private Thread captureThread;
        private bool running;

        private int frameCount = 0;

        public MainWindow()
        {
            InitializeComponent();
            Closing += new System.ComponentModel.CancelEventHandler(OnClosing);

            if (CLNUIDevice.GetDeviceCount() < 1)
            {
                MessageBox.Show("Is the Kinect plugged in?");
                Environment.Exit(0);
            }

            string serialString = CLNUIDevice.GetDeviceSerial(0);
            camera = CLNUIDevice.CreateCamera(serialString);

            colorImage = new NUIImage(640, 480);
            depthImage = new NUIImage(640, 480);
            rawImage = new NUIImage(640, 480);

            color.Source = colorImage.BitmapSource;
            depth.Source = depthImage.BitmapSource;

            running = true;
            captureThread = new Thread(delegate()
            {
                if (CLNUIDevice.StartCamera(camera))
                {
                    while (running)
                    {
                        CLNUIDevice.GetCameraColorFrameRGB32(camera, colorImage.ImageData, 0);
                        CLNUIDevice.GetCameraDepthFrameRGB32(camera, depthImage.ImageData, 0);
                        CLNUIDevice.GetCameraDepthFrameRAW(camera, rawImage.ImageData, 0);
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)delegate()
                        {
                            colorImage.Invalidate();
                            depthImage.Invalidate();
                        });
                    }
                }
            });
            captureThread.IsBackground = true;
            captureThread.Start();
        }

        float RawDepthToMeters(int raw_depth)
        {
            if (raw_depth < 2047)
            {
                return 1.0F / (raw_depth * -0.0030711016F + 3.3309495161F);
            }
            return 0;
        }

        public BitmapSource ProcessDepth(NUIImage depthImageRaw)
        {
            // bytes is width * height * bytes per pixel and depthImageRaw is 16bit (2 bytes)
            byte[] imageBytes = new byte[640 * 480 * 2];

            // Copy the RGB values into the array.
            Marshal.Copy(depthImageRaw.ImageData, imageBytes, 0, imageBytes.Length);

            ushort[] depthFlatArray = new ushort[640 * 480];

            int depthi = 0;

            // go forward two pixels at a time since the depthImage is 16bit
            for (int i = 0; i < imageBytes.Length; i += 2)
            {
                // this is the real height
                ushort h = (ushort)(imageBytes[i] + (imageBytes[i + 1] << 8));
                depthFlatArray[depthi] = h;
                depthi++;
            }

            for (int x = 0; x < 64; x++)
            {
                for (int y = 0; y < 48; y++)
                {
                    depthArray[x, y] = RawDepthToMeters(depthFlatArray[y * 10 * 640 + x * 10]);
                }
            }

            //Creates the new grayscale image
            BitmapSource bmp = BitmapSource.Create(640, 480, 96, 96, System.Windows.Media.PixelFormats.Gray16, null, depthFlatArray, 640 * 2);

            PersistMap(depthArray, "depthmap");

            bmp.Freeze();

            return bmp;
        }

        private void PersistMap(float[,] array, string filename)
        {
            if (frameCount < 240)
                frameCount++;
            else
                frameCount = 0;

            filename = filename + frameCount + ".csv";

            string path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            try
            {
                StreamWriter sw = new StreamWriter(path + "\\Depth\\" + filename);
                for (int x = 0; x < 64; x++)
                {
                    for (int y = 0; y < 48; y++)
                    {
                        sw.Write(array[x, y] + ",");
                    }
                    sw.WriteLine();
                }
                sw.Close();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
            }

        }

        private void PersistMap(int[,] array, string filename)
        {
            if (frameCount < 240)
                frameCount++;
            else
                frameCount = 0;

            filename = filename+frameCount+".csv";

            string path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            try
            {
                StreamWriter sw = new StreamWriter(path + "\\Depth\\" + filename);
                for (int x = 0; x < 64; x++)
                {
                    for (int y = 0; y < 48; y++)
                    {
                        sw.Write(array[x, y] + ",");
                    }
                    sw.WriteLine();
                }
                sw.Close();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
            }
                
        }

        public void Process(NUIImage colorImage)
        {
            int pinWidth = 48;
            int pinHeight = 64;
            int scaleFactor = 10;
            int colorChannels = 4;

            // these pull out the size of the bitmap (should be 640x480 on the Kinect)
            int height = (int)colorImage.BitmapSource.Height;
            int width = (int)colorImage.BitmapSource.Width;
            int stride = width * 4;

            // bytes is width * height * bytes per pixel and depthImageRaw is 16bit (4 bytes)
            byte[] imageBytes = new byte[640 * 480 * 4];
            byte[] imageProcessBytes = new byte[pinWidth * pinHeight * 4];

            // Copy the RGB values into the array.
            Marshal.Copy(colorImage.ImageData, imageBytes, 0, imageBytes.Length);

            for (int pinx = 0; pinx < pinWidth; pinx++)
            {
                for (int piny = 0; piny < pinHeight; piny++)
                {
                    // p is the current pixel
                int sourcep = pinx * (width * colorChannels) * scaleFactor + (piny * colorChannels) * scaleFactor;
                int destp = pinx * (pinWidth * colorChannels) + (piny * colorChannels);
                imageProcessBytes[destp + 0] = imageBytes[sourcep + 0];
                imageProcessBytes[destp + 1] = imageBytes[sourcep + 1];
                imageProcessBytes[destp + 2] = imageBytes[sourcep + 2];
                imageProcessBytes[destp + 3] = 255;// imageBytes[sourcep + 3];

                byte[] bytes = new byte[4];
                bytes[0] = imageBytes[sourcep + 0];
                bytes[1] = imageBytes[sourcep + 1];
                bytes[2] = imageBytes[sourcep + 2];
                bytes[3] = imageBytes[sourcep + 3];

                colorArray[piny, pinx] = BitConverter.ToInt32(bytes, 0);
                }
            }

            PersistMap(colorArray, "colormap");
        }

        void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (captureThread != null)
            {
                running = false;
                captureThread.Join(100);
            }
            try
            {
                if (camera != IntPtr.Zero)
                {
                    //CLNUIDevice.StopCamera(camera);
                    //CLNUIDevice.DestroyCamera(camera);
                }
            }
            catch
            {
                Environment.Exit(0);
            }
            Environment.Exit(0);
        }

        private void refresh_Click(object sender, RoutedEventArgs e)
        {
            DateTime start = DateTime.Now;
            Debug.Print("beginning process " + DateTime.Now);
            for (int i = 0; i < 240; i++)
            {
                Thread.Sleep(15);
                ProcessDepth(rawImage);
                //Process(colorImage, pinImage);
                Process(colorImage);
            }
            Debug.Print("ending process " + start.Subtract(DateTime.Now));
        }

        private void slider_onChange(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sliderLabel != null)
                sliderLabel.Content = slider1.Value;
        }

    }
}
