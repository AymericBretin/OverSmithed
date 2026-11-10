using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace OverSmithed.Editor
{
    /// <summary>
    /// Art direction "matte" (cartoon look, no shine): every material imported with a KayKit model is made
    /// matte — no smoothness, no specular highlight, no sky reflection.
    /// KayKit materials live inside the FBX files and can't be edited by hand, so we change them while Unity
    /// imports the model: nothing in Assets/ThirdParty is modified, and new KayKit models are matte too.
    /// </summary>
    public class KayKitMatteMaterials : AssetPostprocessor
    {
        private const string KayKitFolder = "Assets/ThirdParty/KayKit/";
        private const string LitShaderName = "Universal Render Pipeline/Lit";

        // Bump this number after changing the rules below: Unity then reimports the models with the new rules.
        public override uint GetVersion() => 2;

        // Run after URP's own FBX material converter, so our values are the last word.
        public override int GetPostprocessOrder() => 100;

        // The models import their materials "via MaterialDescription": this is where those materials are built.
        private void OnPreprocessMaterialDescription(MaterialDescription description, Material material,
            AnimationClip[] animations)
        {
            if (!assetPath.StartsWith(KayKitFolder) || material.shader == null || material.shader.name != LitShaderName)
            {
                return;
            }
            material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_SpecularHighlights", 0f);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.SetFloat("_EnvironmentReflections", 0f);
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }
    }
}
