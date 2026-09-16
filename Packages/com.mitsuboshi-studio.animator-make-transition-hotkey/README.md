# Animator Make Transition Hotkey

AnimatorのMake TransitionをTキーで開始します。 正式版 **1.0.0**。Unity 2022.3向けのEditor専用パッケージです。

## VCCに追加

### [▶ クリックしてVCCを開く](https://rinya-mitsuki.github.io/vpm-repository/add.html)

ブラウザーの確認で「開く」を選び、VCCでリポジトリの追加を確定してください。登録済みなら再登録は不要です。Manage Projectから **Animator Make Transition Hotkey** を追加・更新できます。試験版表示の設定は不要です。

VRChat SDKは不要です。

## 旧版からの移行

既存の `AnimatorMakeTransitionHotkey_2022_3_22f1_AllSources.cs`（同じクラスを定義する別版を含む）と対応する `.meta` をプロジェクト外へ退避してから導入してください。共有Editorフォルダー全体を削除しないでください。旧版との二重導入を避けてください。

## 使い方

AnimatorでState・StateMachine・Entry・AnyStateを選択し、Tキーで遷移作成を開始します。Unity内部APIに依存するため、対象環境は2022.3.22f1です。

## バージョンと検証

元ソース `AnimatorMakeTransitionHotkey_2022_3_22f1_AllSources.cs` のバイト列を保持しています。元ファイル名や画面内のv1/v2等は従来の改訂番号で、VPM版のSemVerとは別です。

Unity 2022.3.22f1の参照ライブラリによるC#コンパイルとパッケージ形式を検証しています。Unity Editor内の操作・実動作は未検証です。正式版は配布区分を表し、全環境の動作保証ではありません。

## 利用上の注意・サポート方針

本ツールは作者が完全に個人用として作成したものを、現状のまま公開しています。利用はご自身の判断と責任で行い、実行前にプロジェクトをバックアップしてください。

動作・品質・特定環境への適合性は保証しません。サポート、不具合修正、機能追加、問い合わせへの回答はお約束しません。**利用したことによる苦情は一切受け付けません。**

MITライセンスです。無保証・責任制限は [LICENSE.md](LICENSE.md) を参照してください。
## AIによる制作について

ツール本体、VPMパッケージ化、配布用リポジトリおよび自動化環境は、すべてAIを利用して制作しています。内容を確認したうえで、ご自身の判断と責任で使用してください。

