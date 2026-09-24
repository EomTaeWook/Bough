using System;
using System.IO;
using DataContainer.Generated;

namespace Bough.App
{
    public class TemplateDataLoader
    {
        public void Load()
        {
            string dataPath = Path.Combine(AppContext.BaseDirectory, "Datas");
            TemplateLoader.Load(dataPath, new TemplateDeserializer());
            TemplateLoader.MakeRefTemplate();
        }
    }
}
