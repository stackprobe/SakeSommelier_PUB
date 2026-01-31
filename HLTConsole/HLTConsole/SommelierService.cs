using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HLTStudio.Commons;
using HLTStudio.Modules;
using HLTStudio.Tools;

namespace HLTStudio
{
	public static class SommelierService
	{
		public static string GetSommelierSelectionHTML(string apiKey, string[] selectedTypes, string[] selectedTokuchouArr)
		{
			string strJSONSelectedTypes = JsonTools.GetString(new JsonTools.Node()
			{
				Array = selectedTypes.Select(item => new JsonTools.Node()
				{
					StringValue = item,
				})
				.ToList(),
			});

			string strJSONSelectedTokuchouArr = JsonTools.GetString(new JsonTools.Node()
			{
				Array = selectedTokuchouArr.Select(item => new JsonTools.Node()
				{
					StringValue = item,
				})
				.ToList(),
			});

			string answer = GeminiResponder.Run(
				apiKey,
				$@"

あなたは「酒類レコメンドエンジン」です。
ユーザーは、飲みたい酒のイメージを「自由入力の配列」として渡し、
さらに対象とする「酒の種類の配列」を必ず指定します。

あなたの役割は、
- 自由入力の配列から嗜好・条件を推定し
- 指定された酒の種類の集合の中から
- 条件に合う酒の銘柄候補を選定し
- 指定された JSON スキーマでのみ出力することです。

---

対象となる酒の種類（入力配列に含まれうる値）：
- 日本酒
- ビール
- ワイン
- シードル（りんご酒）
- ミード（はちみつ酒）
- マッコリ
- 紹興酒
- 焼酎
- ウイスキー
- ブランデー
- ウォッカ
- ジン
- ラム
- テキーラ
- リキュール
- 梅酒
- 果実酒
- 薬草酒
- カクテル
- 発泡酒
- ノンアルコール飲料（酒類代替）

カテゴリ選定ルール（厳守）:
- 候補は「指定された酒の種類配列」に含まれるカテゴリからのみ選ぶ。
- 指定配列に含まれないカテゴリの酒を出してはならない。
- 不明・未対応のカテゴリ名が含まれていた場合は notes に記載し、無視する。

---

重要な制約:
- 出力は必ず JSON のみ（説明文・注釈・Markdown・コードブロック禁止）。
- 事実に自信がない項目は推測で埋めず null にする。
- 製造者（酒蔵・醸造所・蒸留所・ワイナリー等）の所在地は
  「都道府県」「市区町村」まで。
- 酒の概略は、味の方向性・香り・おすすめ温度帯・
  合わせたい料理または飲用シーンを1～2文でまとめる。
- 結果は基本5件（条件が厳しい場合は3件でも可）。
- ユーザー条件に矛盾がある場合は notes に記載し、
  必要に応じて解釈を分けて候補を提示してよい。

---

処理手順:
1) 自由入力配列の各要素を独立した条件・ヒントとして解釈する。
2) 味・香り・ボディ・酸・余韻・地域・他酒類嗜好などを総合して
   酒類共通の味わい軸にマッピングする。
3) 指定された酒の種類配列の範囲内で候補を選定する。
4) 下記スキーマに従って JSON を生成する。

---

酒類共通の味わい軸:
- 甘辛: dry / medium / sweet
- ボディ: light / medium / rich
- 香り: subdued / fruity / floral / spicy / herbal
- 透明感・キレ: crisp / clean / smooth
- 酸: low / medium / high
- 余韻: short / medium / long

---

出力スキーマ（厳守）:
{{
  ""query"": {{
    ""user_inputs"": string[],
    ""selected_categories"": string[],
    ""interpreted_preferences"": {{
      ""sweet_dry"": ""dry"" | ""medium"" | ""sweet"" | null,
      ""body"": ""light"" | ""medium"" | ""rich"" | null,
      ""aroma"": ""subdued"" | ""fruity"" | ""floral"" | ""spicy"" | ""herbal"" | null,
      ""clarity"": ""crisp"" | ""clean"" | ""smooth"" | null,
      ""acidity"": ""low"" | ""medium"" | ""high"" | null,
      ""finish"": ""short"" | ""medium"" | ""long"" | null
    }},
    ""keywords"": string[],
    ""constraints"": {{
      ""region_preference"": string | null,
      ""price_range_jpy"": {{
        ""min"": number | null,
        ""max"": number | null
      }}
    }}
  }},
  ""results"": [
    {{
      ""rank"": number,
      ""category"": string,
      ""brand"": string,
      ""producer"": string | null,
      ""producer_location"": {{
        ""prefecture"": string | null,
        ""city"": string | null
      }},
      ""overview"": string,
      ""match_reason"": string[],
      ""confidence"": ""high"" | ""medium"" | ""low""
    }}
  ],
  ""notes"": string[]
}}

---

入力:
- ユーザー自由入力（配列）:
{strJSONSelectedTypes}

- 指定された酒の種類（配列）:
{strJSONSelectedTokuchouArr}

				");

			{
				while (answer[0] != '{')
					answer = answer.Substring(1);

				while (answer[answer.Length - 1] != '}')
					answer = answer.Substring(0, answer.Length - 1);

				answer = JsonTools.GetString(JsonTools.Load(answer));
			}

			string[] lines = File.ReadAllLines(Path.Combine(Consts.RES_DIR, "sommelier-selection.html"), Encoding.UTF8);

			HTMLReplaceLine(lines, "<!-- SET-INITIAL-JSON -->", $"INITIAL_JSON = {answer};");

			return SCommon.LinesToText(lines);
		}

		private static void HTMLReplaceLine(string[] lines, string lineFrom, string lineTo)
		{
			for (int i = 0; i < lines.Length; i++)
				if (lines[i] == lineFrom)
					lines[i] = lineTo;
		}
	}
}
