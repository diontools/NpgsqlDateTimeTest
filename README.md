# NpgsqlのDateTimeの扱い

Npgsql v6でDateTimeの扱いが変更されたので調査。


## 参考資料
* https://www.npgsql.org/doc/types/datetime.html
* https://www.npgsql.org/doc/release-notes/6.0.html#timestamp-rationalization-and-improvements
* https://www.roji.org/postgresql-dotnet-timestamp-mapping


## 動作まとめ
* v6 とそれ以降
  * `Kind` は `Utc` のみ受け付け、それ以外は `InvalidCastException`
  * timestamp without time zone を読み取ると `Unspecified`、+9時間(PostgreSQLのタイムゾーン)
  * timestamp with time zone を読み取ると `Utc`
  * date を読み取ると `Unspecified`、+9時間(PostgreSQLのタイムゾーン)
* v6 より前
  * `Kind` に依存しない
  * timestamp without time zone を読み取ると `Unspecified`
  * timestamp with time zone を読み取ると `Local`
  * date を読み取ると `Unspecified`

> v6 より前では、timestamp with time zone はかなり奇妙な動作をする場合がある。後述のテスト結果を参照。
PostgreSQLタイムゾーンがUTC-5、ローカルがUTC+9のとき、UTC+14となってしまう。
> この動作を上手く理解することができなかったが、基本的にタイムゾーンはそろえるべきなのであろう。


## 覚えておくこと
* PostgreSQLはUTCが基本である "UTC everywhere"
  * ⚠️ timestamp with time zone はタイムゾーン情報を持っておらず、UTCで記録され、テキスト表現は接続のタイムゾーンに変換される
  * timestamp without time zone もタイムゾーン情報を持っておらず、UTCで記録され、読み取り時に接続のタイムゾーンに変換される
* .NET の `DateTime` 型の比較は `Ticks` の値で行われる（`Kind` の違いは無視される）
* 接続文字列でPostgreSQLのタイムゾーンを指定できる `TimeZone=Asia/Tokyo`
  * 指定しない場合はPostgreSQLの設定値が使用される


## すべきこと
* 可能な限りUTCを使う
  * timestamp without time zone は使わない
  * date は `DateOnly` 型を使うべき (.NET 6以降)
* ローカル時間にしたい場合は
  * PostgreSQLのタイムゾーンとローカルのタイムゾーンが同じであることを前提に (異なる場合は接続文字列で指定する)
  * 読み取った `DateTime` が `Unspecified` のとき `Local` として扱う: `new DateTime(dt.Ticks, DateTimeKind.Local)`
  * 読み取った `DateTime` が `Utc` のとき `Local` に変換する: `dt.ToLocalTime()`
  * `DateTime` をパラメーターに渡すとき `Utc` に変換する: `dt.ToUniversalTime()`
  * ただし、日付が変わる可能性があることに注意する

互換スイッチで旧動作に戻すことができるが最終手段である。（将来的に削除される可能性がある）
```cs
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
```


# 調査環境
* .NET 7
* Npgsql v7.0.2
* PostgreSQLのタイムゾーンは `Asia/Tokyo` \[UTC+9]
* ローカルのタイムゾーンは日本

### docker-compose.yml
```yml
version: "3.8"
services:
  postgres13x:
    container_name: postgres13x
    image: postgres:13-alpine
    restart: "no"
    volumes:
      - postgres13x-data:/var/lib/postgresql/data
    ports:
      - 15432:5432
    environment:
      POSTGRES_PASSWORD: postgres
      TZ: Asia/Tokyo

volumes:
  postgres13x-data:
```

### 実行方法
```
dotnet run
dotnet run legacy
```

# 結果
※ 強調は変化した値(Ticks)

## 新(v6とそれ以降)
| Input | without time zone | with time zone | date |
| ----- | ----------------- | -------------- | ---- |
| 2000/01/01 0:00:00,Utc | **2000/01/01 9:00:00,Unspecified** | 2000/01/01 0:00:00,Utc | 2000/01/01 0:00:00,Unspecified |
| 2000/01/01 0:00:00,Local | System.InvalidCastException | System.InvalidCastException | System.InvalidCastException |
| 2000/01/01 0:00:00,Unspecified | System.InvalidCastException | System.InvalidCastException | System.InvalidCastException |
| 2000/01/01 21:00:00,Utc | **2000/01/02 6:00:00,Unspecified** | 2000/01/01 21:00:00,Utc | **2000/01/02 0:00:00,Unspecified** |
| 2000/01/01 21:00:00,Local | System.InvalidCastException | System.InvalidCastException | System.InvalidCastException |
| 2000/01/01 21:00:00,Unspecified | System.InvalidCastException | System.InvalidCastException | System.InvalidCastException |

## 旧(v6より前)
| Input | without time zone | with time zone | date |
| ----- | ----------------- | -------------- | ---- |
| 2000/01/01 0:00:00,Utc | 2000/01/01 0:00:00,Unspecified | 2000/01/01 0:00:00,Local | 2000/01/01 0:00:00,Unspecified |
| 2000/01/01 0:00:00,Local | 2000/01/01 0:00:00,Unspecified | 2000/01/01 0:00:00,Local | 2000/01/01 0:00:00,Unspecified |
| 2000/01/01 0:00:00,Unspecified | 2000/01/01 0:00:00,Unspecified | 2000/01/01 0:00:00,Local | 2000/01/01 0:00:00,Unspecified |
| 2000/01/01 21:00:00,Utc | 2000/01/01 21:00:00,Unspecified | 2000/01/01 21:00:00,Local | **2000/01/01 0:00:00,Unspecified** |
| 2000/01/01 21:00:00,Local | 2000/01/01 21:00:00,Unspecified | 2000/01/01 21:00:00,Local | **2000/01/01 0:00:00,Unspecified** |
| 2000/01/01 21:00:00,Unspecified | 2000/01/01 21:00:00,Unspecified | 2000/01/01 21:00:00,Local | **2000/01/01 0:00:00,Unspecified** |


# 結果 (PostgreSQLタイムゾーンがAmerica/New_York\[UTC-5]の場合)

## 新(v6とそれ以降)
| Input | without time zone | with time zone | date |
| ----- | ----------------- | -------------- | ---- |
| 2000/01/01 0:00:00,Utc | **1999/12/31 19:00:00,Unspecified** | 2000/01/01 0:00:00,Utc | **1999/12/31 0:00:00,Unspecified** |
| 2000/01/01 0:00:00,Local | System.InvalidCastException | System.InvalidCastException | System.InvalidCastException |
| 2000/01/01 0:00:00,Unspecified | System.InvalidCastException | System.InvalidCastException | System.InvalidCastException |
| 2000/01/01 21:00:00,Utc | **2000/01/01 16:00:00,Unspecified** | 2000/01/01 21:00:00,Utc | **2000/01/01 0:00:00,Unspecified** |
| 2000/01/01 21:00:00,Local | System.InvalidCastException | System.InvalidCastException | System.InvalidCastException |
| 2000/01/01 21:00:00,Unspecified | System.InvalidCastException | System.InvalidCastException | System.InvalidCastException |

## 旧(v6より前)
| Input | without time zone | with time zone | date |
| ----- | ----------------- | -------------- | ---- |
| 2000/01/01 0:00:00,Utc | 2000/01/01 0:00:00,Unspecified | **2000/01/01 14:00:00,Local** | 2000/01/01 0:00:00,Unspecified |
| 2000/01/01 0:00:00,Local | 2000/01/01 0:00:00,Unspecified | **2000/01/01 14:00:00,Local** | 2000/01/01 0:00:00,Unspecified |
| 2000/01/01 0:00:00,Unspecified | 2000/01/01 0:00:00,Unspecified | **2000/01/01 14:00:00,Local** | 2000/01/01 0:00:00,Unspecified |
| 2000/01/01 21:00:00,Utc | 2000/01/01 21:00:00,Unspecified | **2000/01/02 11:00:00,Local** | **2000/01/01 0:00:00,Unspecified** |
| 2000/01/01 21:00:00,Local | 2000/01/01 21:00:00,Unspecified | **2000/01/02 11:00:00,Local** | **2000/01/01 0:00:00,Unspecified** |
| 2000/01/01 21:00:00,Unspecified | 2000/01/01 21:00:00,Unspecified | **2000/01/02 11:00:00,Local** | **2000/01/01 0:00:00,Unspecified** |

