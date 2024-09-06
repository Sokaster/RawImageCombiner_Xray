using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using Dicom;
using Dicom.Imaging;
using Dicom.IO.Buffer;
using OpenCvSharp;
using System.Runtime.InteropServices;

namespace RawImageCombiner
{
    public partial class MainWindow : System.Windows.Window
    {
        private string[] selectedFilePaths;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnSelectFilesButtonClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "RAW files (*.raw)|*.raw|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                selectedFilePaths = openFileDialog.FileNames;
                UpdateUIAfterSelection();

                CombineButton.IsEnabled = true;
                NormalizeAndConvertButton.IsEnabled = true; // Активируем кнопку нормировки и конвертации
            }
        }
        private void NormalizeAndConvertToDicom(string filePath, string outputFilePath, int imageWidth, int imageHeight)
        {
            try
            {
                long totalPixels = (long)imageWidth * imageHeight;
                ushort[] fullImage = new ushort[totalPixels];

                using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] rawData = new byte[totalPixels * sizeof(ushort)];
                    fileStream.Read(rawData, 0, rawData.Length);

                    ushort[] imageData = new ushort[totalPixels];
                    Buffer.BlockCopy(rawData, 0, imageData, 0, rawData.Length);

                    // Преобразуем ushort[] в byte[] для добавления в DicomPixelData
                    byte[] byteArray = new byte[imageData.Length * sizeof(ushort)];
                    Buffer.BlockCopy(imageData, 0, byteArray, 0, byteArray.Length);

                    // Создаем пустой Mat и копируем данные
                    using (Mat imageMat = new Mat(imageHeight, imageWidth, MatType.CV_16UC1))
                    {
                        Marshal.Copy(byteArray, 0, imageMat.Data, byteArray.Length);

                        // Применяем медианный фильтр для удаления шумов
                        Cv2.MedianBlur(imageMat, imageMat, 3);

                        // Конвертируем обратно в ushort[]
                        imageMat.GetArray(out ushort[] normalizedImage);

                        for (long y = 0; y < imageHeight; y++)
                        {
                            for (long x = 0; x < imageWidth; x++)
                            {
                                fullImage[y * imageWidth + x] = normalizedImage[y * imageWidth + x];
                            }
                        }
                    }
                }

                // Сохранение изображения в DICOM формат
                SaveAsDicom(fullImage, imageWidth, imageHeight, outputFilePath);

                // Очистка массива fullImage, если он больше не нужен
                Array.Clear(fullImage, 0, fullImage.Length);
            }
            catch (OverflowException ex)
            {
                MessageBox.Show($"Произошла ошибка переполнения: {ex.Message}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Произошла ошибка при нормировке и конвертации файла: {ex.Message}");
            }
            finally
            {
                // Принудительно вызываем сборщик мусора для освобождения неиспользуемой памяти
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        private void OnNormalizeAndConvertButtonClick(object sender, RoutedEventArgs e)
{
    // Открываем диалог для выбора файла изображения
    OpenFileDialog openFileDialog = new OpenFileDialog
    {
        Filter = "RAW files (*.raw)|*.raw|All files (*.*)|*.*"
    };

    if (openFileDialog.ShowDialog() == true)
    {
        string selectedImageFilePath = openFileDialog.FileName;

        if (!int.TryParse(WidthTextBox.Text, out int imageWidth) || !int.TryParse(HeightTextBox.Text, out int imageHeight))
        {
            MessageBox.Show("Пожалуйста, введите корректные размеры изображения.");
            return;
        }

        SaveFileDialog saveFileDialog = new SaveFileDialog
        {
            Filter = "DICOM file (*.dcm)|*.dcm",
            DefaultExt = "dcm",
            FileName = "normalized_image.dcm"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            // Убедитесь, что размеры корректны и могут быть обработаны
            if (imageWidth <= 0 || imageHeight <= 0)
            {
                MessageBox.Show("Ширина и высота изображения должны быть больше 0.");
                return;
            }

            NormalizeAndConvertToDicom(selectedImageFilePath, saveFileDialog.FileName, imageWidth, imageHeight);
            MessageBox.Show("Нормировка и конвертация завершена!");
        }
    }
}

        private void SaveAsDicom(ushort[] imageData, int width, int height, string outputFilePath)
        {
            var dicomDataset = new DicomDataset
    {
        { DicomTag.PatientID, "123456" },
        { DicomTag.StudyInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID().UID },
        { DicomTag.SeriesInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID().UID },
        { DicomTag.SOPInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID().UID },
        { DicomTag.SOPClassUID, DicomUID.SecondaryCaptureImageStorage }
    };

            dicomDataset.AddOrUpdate(DicomTag.Rows, (ushort)height);
            dicomDataset.AddOrUpdate(DicomTag.Columns, (ushort)width);
            dicomDataset.AddOrUpdate(DicomTag.BitsAllocated, (ushort)16);
            dicomDataset.AddOrUpdate(DicomTag.BitsStored, (ushort)16);
            dicomDataset.AddOrUpdate(DicomTag.HighBit, (ushort)15);
            dicomDataset.AddOrUpdate(DicomTag.SamplesPerPixel, (ushort)1);
            dicomDataset.AddOrUpdate(DicomTag.PhotometricInterpretation, PhotometricInterpretation.Monochrome2.Value);
            dicomDataset.AddOrUpdate(DicomTag.PixelRepresentation, (ushort)0);

            var pixelData = DicomPixelData.Create(dicomDataset, true);
            pixelData.PlanarConfiguration = 0;

            byte[] byteArray = new byte[imageData.Length * 2];
            Buffer.BlockCopy(imageData, 0, byteArray, 0, byteArray.Length);

            var buffer = new MemoryByteBuffer(byteArray);
            pixelData.AddFrame(buffer);

            var dicomFile = new DicomFile(dicomDataset);
            dicomFile.Save(outputFilePath);
        }

        private void UseStepCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            StepComboBox.IsEnabled = true;
        }

        private void UseStepCheckbox_Unchecked(object sender, RoutedEventArgs e)
        {
            StepComboBox.IsEnabled = false;
        }

        private void OnCombineButtonClick(object sender, RoutedEventArgs e)
        {
            if (selectedFilePaths == null || selectedFilePaths.Length == 0)
            {
                MessageBox.Show("Пожалуйста, выберите файлы для объединения.");
                return;
            }

            int imageWidth = 72; // ширина одного изображения
            int imageHeight = 2304; // высота одного изображения

            // Применяем шаг, если необходимо
            if (UseStepCheckbox.IsChecked == true && StepComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                int step = int.Parse((string)selectedItem.Tag);
                selectedFilePaths = selectedFilePaths.Where((path, index) => index % step == 0).ToArray();
            }

            string outputFileName;

            if (SelectMode1.IsChecked == true)
            {
                // Режим 1: Склейка всех изображений
                int fullWidth = imageWidth * selectedFilePaths.Length;
                outputFileName = $"combined_image_{fullWidth}x{imageHeight}";

                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "RAW file (*.raw)|*.raw",
                    DefaultExt = "raw",
                    FileName = outputFileName
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    string rawFilePath = saveFileDialog.FileName;
                    CombineRawImages(selectedFilePaths, rawFilePath, imageWidth, imageHeight);
                    MessageBox.Show("Объединение завершено!");

                    // Если чекбокс отмечен, создаем DICOM файл
                    if (CreateDicomCheckbox.IsChecked == true)
                    {
                        string dicomFilePath = Path.ChangeExtension(rawFilePath, ".dcm");
                        NormalizeAndConvertToDicom(rawFilePath, dicomFilePath, fullWidth, imageHeight);
                        MessageBox.Show("DICOM файл создан!");
                    }
                }
            }
            else if (SelectMode2.IsChecked == true)
            {
                // Режим 2: Склейка определенного количества пикселей
                int pixelCount = int.Parse((string)((ComboBoxItem)PixelCountComboBox.SelectedItem).Tag);
                bool isLeftSide = LeftSideRadioButton.IsChecked == true;

                int fullWidth = pixelCount * selectedFilePaths.Length;
                outputFileName = $"combined_pixels_{fullWidth}x{imageHeight}";

                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "RAW file (*.raw)|*.raw",
                    DefaultExt = "raw",
                    FileName = outputFileName
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    string rawFilePath = saveFileDialog.FileName;
                    CombineSelectedPixels(selectedFilePaths, rawFilePath, imageWidth, imageHeight, pixelCount, isLeftSide);
                    MessageBox.Show("Объединение пикселей завершено!");

                    // Создаем DICOM файл вместе с сырым файлом
                    string dicomFilePath = Path.ChangeExtension(rawFilePath, ".dcm");
                    NormalizeAndConvertToDicom(rawFilePath, dicomFilePath, fullWidth, imageHeight);
                    MessageBox.Show("DICOM файл создан!");
                }
            }
        }

        private void CombineRawImages(string[] filePaths, string outputFilePath, int imageWidth, int imageHeight)
        {
            int fullWidth = imageWidth * filePaths.Length;
            ushort[] fullImage = new ushort[fullWidth * imageHeight];

            for (int i = 0; i < filePaths.Length; i++)
            {
                using (FileStream fileStream = new FileStream(filePaths[i], FileMode.Open, FileAccess.Read))
                {
                    byte[] rawData = new byte[imageWidth * imageHeight * 2];
                    fileStream.Read(rawData, 0, rawData.Length);

                    ushort[] imageData = new ushort[imageWidth * imageHeight];
                    Buffer.BlockCopy(rawData, 0, imageData, 0, rawData.Length);

                    for (int y = 0; y < imageHeight; y++)
                    {
                        for (int x = 0; x < imageWidth; x++)
                        {
                            fullImage[y * fullWidth + (i * imageWidth + x)] = imageData[y * imageWidth + x];
                        }
                    }
                }
            }

            SaveRawImage(fullImage, fullWidth, imageHeight, outputFilePath);
        }

        private void CombineSelectedPixels(string[] filePaths, string outputFilePath, int imageWidth, int imageHeight, int pixelCount, bool isLeftSide)
        {
            int fullWidth = pixelCount * filePaths.Length;
            ushort[] fullImage = new ushort[fullWidth * imageHeight];

            for (int i = 0; i < filePaths.Length; i++)
            {
                using (FileStream fileStream = new FileStream(filePaths[i], FileMode.Open, FileAccess.Read))
                {
                    byte[] rawData = new byte[imageWidth * imageHeight * 2];
                    fileStream.Read(rawData, 0, rawData.Length);

                    ushort[] imageData = new ushort[imageWidth * imageHeight];
                    Buffer.BlockCopy(rawData, 0, imageData, 0, rawData.Length);

                    int startX = isLeftSide ? 0 : (imageWidth - pixelCount);

                    for (int y = 0; y < imageHeight; y++)
                    {
                        for (int x = 0; x < pixelCount; x++)
                        {
                            fullImage[y * fullWidth + (i * pixelCount + x)] = imageData[y * imageWidth + (startX + x)];
                        }
                    }
                }
            }

            SaveRawImage(fullImage, fullWidth, imageHeight, outputFilePath);

            // Display final pixel length to the user
            MessageBox.Show($"Длина суммированных пикселей: {fullWidth} px");
        }

        private void SaveRawImage(ushort[] imageData, int width, int height, string outputFilePath)
        {
            try
            {
                // Проверяем размер изображения
                long totalSize = (long)imageData.Length * sizeof(ushort); // Общий размер данных в байтах

                // Ограничиваем размер буфера для записи
                int bufferSize = 1024 * 1024; // 1 MB
                byte[] buffer = new byte[bufferSize];

                using (FileStream fileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
                {
                    int offset = 0;
                    while (offset < totalSize)
                    {
                        int bytesToWrite = (int)Math.Min(bufferSize, totalSize - offset);

                        // Копируем часть данных в буфер
                        Buffer.BlockCopy(imageData, offset, buffer, 0, bytesToWrite);
                        fileStream.Write(buffer, 0, bytesToWrite);

                        offset += bytesToWrite;
                    }
                }
            }
            catch (OverflowException ex)
            {
                MessageBox.Show($"Произошла ошибка переполнения: {ex.Message}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Произошла ошибка при сохранении файла: {ex.Message}");
            }
        }


        private void UpdateUIAfterSelection()
        {
            int selectedCount = selectedFilePaths.Length;
            SelectedFilesTextBlock.Text = $"Выбрано файлов: {selectedCount}";

            int step = 1;
            if (UseStepCheckbox.IsChecked == true && StepComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                step = int.Parse((string)selectedItem.Tag);
            }

            int effectiveFileCount = selectedCount / step;
            int imageWidth = 72; // ширина одного изображения
            int calculatedLength = effectiveFileCount * imageWidth;

            CalculatedLengthTextBlock.Text = $"Рассчитанная длина итогового изображения: {calculatedLength} px";

            // Активируем кнопки, если выбрано хотя бы одно изображение
            if (selectedCount > 0)
            {
                CombineButton.IsEnabled = true;
                NormalizeAndConvertButton.IsEnabled = true;
            }
        }
    }
}
