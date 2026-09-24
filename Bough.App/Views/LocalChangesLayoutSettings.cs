using System;
using System.IO;
using System.Text.Json;
using Dignus.Log;

namespace Bough.App.Views
{
    internal class LocalChangesLayoutSettings
    {
        private const double DefaultFileListWidth = 310;
        private const double MinimumFileListWidth = 260;
        private const double MaximumFileListWidth = 1200;
        private const double DefaultStagedHeightRatio = 0.5;

        private readonly string _filePath;
        private readonly string _stagedHeightPath;

        public LocalChangesLayoutSettings()
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bough", "local-changes-layout.json"))
        {
        }

        internal LocalChangesLayoutSettings(string filePath)
        {
            _filePath = filePath;
            _stagedHeightPath = Path.ChangeExtension(filePath, ".staged-height.json");
        }

        public double LoadFileListWidth()
        {
            return LoadValue(_filePath, DefaultFileListWidth, MinimumFileListWidth, MaximumFileListWidth);
        }

        public double LoadStagedHeightRatio()
        {
            double ratio = LoadValue(_stagedHeightPath, DefaultStagedHeightRatio, 0, 1);
            if (ratio <= 0)
            {
                return DefaultStagedHeightRatio;
            }

            if (ratio >= 1)
            {
                return DefaultStagedHeightRatio;
            }

            return ratio;
        }

        public void SaveFileListWidth(double width)
        {
            SaveValue(_filePath, width, MinimumFileListWidth, MaximumFileListWidth);
        }

        public void SaveStagedHeightRatio(double ratio)
        {
            if (ratio <= 0)
            {
                return;
            }

            if (ratio >= 1)
            {
                return;
            }

            SaveValue(_stagedHeightPath, ratio, 0, 1);
        }

        private static double LoadValue(string filePath, double defaultValue, double minimum, double maximum)
        {
            try
            {
                if (File.Exists(filePath) == false)
                {
                    return defaultValue;
                }

                double value = JsonSerializer.Deserialize<double>(File.ReadAllText(filePath));
                if (double.IsFinite(value) == false)
                {
                    return defaultValue;
                }

                if (value < minimum || value > maximum)
                {
                    return defaultValue;
                }

                return value;
            }
            catch (Exception exception) when (exception is JsonException || exception is IOException || exception is UnauthorizedAccessException)
            {
                LogHelper.Error(exception);
                return defaultValue;
            }
        }

        private static void SaveValue(string filePath, double value, double minimum, double maximum)
        {
            if (double.IsFinite(value) == false)
            {
                return;
            }

            if (value < minimum || value > maximum)
            {
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(filePath);
                if (directory == null)
                {
                    return;
                }

                Directory.CreateDirectory(directory);
                File.WriteAllText(filePath, JsonSerializer.Serialize(value));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                LogHelper.Error(exception);
            }
        }
    }
}
