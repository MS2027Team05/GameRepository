// GitHub Actionsのdotnet formatによる.editorconfigのフォーマット遵守されているかどうかのチェック用コード
// 本ファイルは第1段階: 意図的にフォーマット・命名規則違反を含めたテストケースです。
// CI(check_format.yml)が正しく違反を検知するかを検証するために使用します。

namespace DummyTest {

// 違反1: アクセス修飾子なし(IDE0040違反)
// 違反2: 命名規則違反(クラス名がcamelCase)
// 違反3: 波括弧が改行されていない
class invalidNamingClass {

    // 違反4: インデントがタブではなくスペース4つ
    // 違反5: アクセス修飾子なし
    // 違反6: フィールド名に 'm_' 接頭辞がなくcamelCase
    int valueCount = 0;

    // 違反7: アクセス修飾子なし
    // 違反8: メソッド名がcamelCase
    // 違反9: 引数名がPascalCase(camelCase違反)
    void executeCalculation(int BaseValue) {
        // 違反10: 制御構文キーワード直後のスペース欠落 'if('
        // 違反11: 二項演算子の前後にスペースがない '1+2'
        if(BaseValue>0) {
            valueCount = BaseValue+10;
        }
    }
}

// 違反12: インターフェース名に 'I' 接頭辞がない
public interface ServiceRunner {
    void Run();
}

// 違反13: 抽象クラス名に 'A' 接頭辞がない
public abstract class BaseManager {
    public abstract void Initialize();
}

// 違反14: 列挙型名に 'E' 接頭辞がない
public enum GameStatus {
    // 違反15: 列挙型メンバーがsnake_case(PascalCase違反)
    in_progress,
    completed
}

}
