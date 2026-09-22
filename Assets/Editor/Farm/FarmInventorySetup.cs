using System.IO;
using UnityEditor;
using UnityEngine;
namespace Farm.EditorTools
{
    [InitializeOnLoad]
    public static class FarmInventorySetup
    {
        static FarmInventorySetup(){EditorApplication.update+=Poll;}
        private static void Poll()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||!File.Exists("Library/FarmInventorySetup.request"))return;
            File.Delete("Library/FarmInventorySetup.request");Build();
        }
        [MenuItem("Tools/Farm/Import Inventory UI")]
        public static void Build()
        {
            const string folder="Assets/Farm/Resources/FarmInventory";
            Directory.CreateDirectory(folder);
            foreach(string name in new[]{"Inventory","Icons"})
            {
                string path=folder+"/"+name+".png";
                File.Copy("Assets/Tileset/UI/"+name+".png",path,true);
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType=TextureImporterType.Default;importer.filterMode=FilterMode.Point;
                importer.textureCompression=TextureImporterCompression.Uncompressed;importer.mipmapEnabled=false;
                importer.alphaIsTransparency=true;importer.npotScale=TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
            }
            File.WriteAllText("BuildArtifacts/inventory-setup.txt","PASS");
        }
    }
}
