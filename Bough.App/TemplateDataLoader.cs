using System;
using System.IO;
using DataContainer.Generated;

namespace Bough.App
{
    public class TemplateDataLoader
    {
        public void Load()
        {
            TemplateLoader.Load(ReadJson, new TemplateDeserializer());
            TemplateLoader.MakeRefTemplate();
        }

        private static string ReadJson(string fileName)
        {
#if DEBUG
            string filePath = Path.Combine(AppContext.BaseDirectory, "Datas", fileName);
            if (File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }
#endif
            return PackagedResources.ReadText($"Bough.Datas.{fileName}");
        }
    }
}
