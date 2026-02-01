using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HLTStudio.Commons;
using HLTStudio.Tools;

namespace HLTStudio.Modules
{
	public class GeminiResponder
	{
		// default Gemini API Key 取得手順 (簡易メモ)
		// クレジットカード：不要 (無料枠あり)
		//
		// ① Google AI Studio にアクセス
		// https://aistudio.google.com/
		// ★ Google アカウントでログインする
		//
		// ② APIキー管理画面を開く
		// https://aistudio.google.com/api-keys
		//
		// ③ APIキーを作成
		// 画面右上の
		// 「APIキーを作成」をクリック
		// -> 特に設定を変更せず、そのまま作成してOK
		// -> 「Default Gemini API Key」が作られる
		//
		// ④ APIキーをコピー
		// 表示された APIキーをコピー
		// このキーを、ツールの設定画面に貼り付ける
		// ★APIキーは他人に公開しないこと
		// (GitHub / スクショ / SNS などに載せない)

		//private static string API_KEY = "AIza***********************************";

		public static string Run(string apiKey, string question)
		{
			if (
				string.IsNullOrWhiteSpace(apiKey) ||
				string.IsNullOrWhiteSpace(question)
				)
				throw new Exception("Bad params");

			using (WorkingDir wd = new WorkingDir())
			{
				string sndMsg;

				sndMsg = question;
				//sndMsg = "こんにちは。動作確認です";
				//sndMsg = "東京の日本酒を紹介してください。";
				/*
				sndMsg = @"

あなたは日本酒の基礎データを整理するプログラムです。

東京都で生産されている日本酒を取得してください。

制約：
- 推測で作らない
- 実在が不確かなものは含めない
- 該当がない場合は空配列を返す
- 出力は JSON のみ
- JSON 以外の文字は一切出力しない

出力形式：
[
	{
		""name"": ""銘柄名"",
		""brewery"": ""酒蔵名""
	}
]

					";
					//*/

				string sendFile = wd.MakePath();
				string recvFile = wd.MakePath();

				File.WriteAllBytes(sendFile, Encoding.UTF8.GetBytes($@"

{{
	""contents"": [
		{{
			""role"": ""user"",
			""parts"": [
				{{
					""text"":
						""{EncodeJSONString(sndMsg)}""
				}}
			]
		}}
	]
}}

					".Trim()));

#if false // 応答例

{
  "candidates": [
    {
      "content": {
        "parts": [
          {
            "text": "こんにちは！動作確認、ありがとうございます。\n\n何かお手伝いできることはありますか？ 例えば、\n\n*   **質問があれば、何でも聞いてください。**\n*   **特定のタスク（文章作成、翻訳、要約など）を試したいですか？**\n*   **どのような反応を期待されているか教えていただけると、より的確にお答えできます。**\n\nお気軽にお申し付けください！"
          }
        ],
        "role": "model"
      },
      "finishReason": "STOP",
      "index": 0
    }
  ],
  "usageMetadata": {
    "promptTokenCount": 6,
    "candidatesTokenCount": 85,
    "totalTokenCount": 91,
    "promptTokensDetails": [
      {
        "modality": "TEXT",
        "tokenCount": 6
      }
    ]
  },
  "modelVersion": "gemini-2.5-flash-lite",
  "responseId": "1bN6aeOOKL3k1e8P2vHIuAY"
}

#endif

				string[] URLS_BY_MODEL = new string[]
				{
					"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent", // 標準？
					"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite:generateContent", // 軽量版？
				};

				string url = SCommon.CRandom.ChooseOne(URLS_BY_MODEL); // zantei

				HTTPClient hc = new HTTPClient(url)
				{
					ConnectTimeoutMillis = 30_000, // 30 sec
					TimeoutMillis = 1200_000, // 20 min
					IdleTimeoutMillis = 900_000, // 15 min
					ResBodySizeMax = 50_000_000, // 50 MB
					ResFile = recvFile,
				};

				hc.GetInner().ContentType = "application/json";
				hc.AddHeader("x-goog-api-key", apiKey);

				hc.Post(sendFile);

				//Console.WriteLine(Encoding.UTF8.GetString(File.ReadAllBytes(recvFile)));
				//File.Copy(recvFile, SCommon.NextOutputPath() + ".txt");

				JsonTools.Node root = JsonTools.LoadFromFile(recvFile);
				string answer = root["candidates"][0]["content"]["parts"][0]["text"].StringValue;

				//Console.WriteLine(answer);

				return answer;
			}
		}

		private static string EncodeJSONString(string text)
		{
			text = text.Trim();

			text = text.Replace("\r", "");
			text = text.Replace("\n", "\\n");
			text = text.Replace("\t", "\\t");
			text = text.Replace("\"", "\\\"");

			return text;
		}
	}
}
