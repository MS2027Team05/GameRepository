using UnityEditor;
using TMPro;

public class TMPAssetCleaner : AssetModificationProcessor
{
    // 保存（Ctrl+Sやビルド時）の直前に呼ばれる
    private static string[] OnWillSaveAssets(string[] paths)
    {
        foreach (string path in paths)
        {
            if (path.EndsWith(".asset"))
            {
                var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                // 対象のDynamicフォントアセットかどうかを名前などで判定
                if (fontAsset != null && fontAsset.name.Contains("DynamicFallback"))
                {
                    // 1. 生成されたテクスチャデータをクリア
                    fontAsset.ClearFontAssetData();

                    // 2. 文字テーブルをクリア
                    if (fontAsset.characterTable != null)
                        fontAsset.characterTable.Clear();

                    // 3. グリフテーブルをクリア
                    if (fontAsset.glyphTable != null)
                        fontAsset.glyphTable.Clear();

                    // 4. (必要に応じて) カーニング等の動的データもリセット
                    // これによりアセットファイルが「初期状態」のバイナリに戻ります
                    
                    // 変更を確定させる
                    EditorUtility.SetDirty(fontAsset);
                }
            }
        }
        return paths;
    }
}


// Fallbackを使った効率の良い管理についてのメモ
// 1. まずは普段通りFontAtlasを作ります。この時、ひらがなやカタカナ、英数字に加えてJIS第一水準まで記載しておくと良いかも(第二まで入れると使わない文字が一気に増えるので。使う文字もあるけどね)。この場合は4096x4096ではなく2048x4096程度で済むと思います。
// (ここに設定時のpixel等々のサイズの情報が欲しいね)
// 2. FallbackのAssetを作ります。元フォント部分を右クリックし、Create->TextMeshPro->FontAsset->SDFで元データを作成。作成したもののInspector側の設定、GenerationSettingsを1で作ったものと合わせ、`Atlas PopulationMode`を`Dynamic`にするのと、MultiAtlasTextureをonにする(Atlasのサイズは1024x1024程度で良いかも)
// 3. 1で作成したTextMeshProのデータを開き、Inspectorにある`FallbackFontAssets`の欄に、2で作成したフォントデータを入れてください。
// 4. 実際にテキストを召喚し、導入したフォントにしてから、生成範囲外の文字を入力してください。ちなみに、Fallbackに登録した関係でデフォルトからフォントを変えずとも文字の表示が行われてしまい、その場合は全フォントがDynamicに表示されてしまうので処理効率が低下してしまう。
// 5. このスクリプトをどこかにEditorファイルを作り、その下に配置する(git追跡防止)。ビルド時にこのスクリプトが勝手に走り、ディスク上でのデータが常に空になるためgitで追跡されなくなります。
// 6. もし頻繁に表示する文字がある場合はFontAtlas作成時にその文字だけ別個で追加してあげてね。Dynamicで表示する量が増えると効率が下がるのであくまでも予備用。