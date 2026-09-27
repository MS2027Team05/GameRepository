// GitHub Actionsのdotnet formatによる.editorconfigのフォーマット遵守されているかどうかのチェック用コード
// 本ファイルは第2段階: .editorconfig の全規約を完全に遵守したテストケースです。
// CI(check_format.yml)でフォーマット差分が発生せず、正常にパスすることを検証するために使用します。

namespace DummyTest
{
	public interface IDummyService
	{
		void Run();
	}

	public abstract class ADummyBase
	{
		public abstract void Initialize();
	}

	public enum EDummyState
	{
		InProgress,
		Completed
	}

	public class DummyScript : ADummyBase, IDummyService
	{
		private int m_ValueCount = 0;

		public int ValueCount
		{
			get => m_ValueCount;
			set => m_ValueCount = value;
		}

		public override void Initialize()
		{
			m_ValueCount = 0;
		}

		public void Run()
		{
			ExecuteCalculation(10);
		}

		public void ExecuteCalculation(int baseValue)
		{
			if (baseValue > 0)
			{
				m_ValueCount = baseValue + 10;
			}
		}
	}
}
