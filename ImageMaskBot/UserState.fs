module UserState

open System
open System.Data
open System.Data.SQLite

open FunogramHelpers

type Database = SQLiteConnection

type State =
    | Initial = 0
    | PromptedForBase = 1
    | BaseSet = 2
    | PromptedForMask = 3
    | MaskSet = 4
    | Ready = 5

type ImageType =
    | Base = 0
    | Mask = 1

type DatabaseUser =
    { Id: int64
      UserId: int64
      Username: string }

type UserState =
    { Id: int64
      User: DatabaseUser
      State: State }

type Image =
    { Id: int64
      Guid: Guid }

type UserImage =
    { Id: int64
      User: DatabaseUser
      Image: Image
      Type: ImageType }

let private createTables (connection: SQLiteConnection) =
    let sql = new SQLiteCommand(connection)
    sql.CommandText <- "CREATE TABLE IF NOT EXISTS users (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        userid INTEGER UNIQUE,
        username TEXT
    )"
    sql.ExecuteNonQuery() |> ignore
    let sql = new SQLiteCommand(connection)
    sql.CommandText <- "CREATE TABLE IF NOT EXISTS userstates (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        user INTEGER UNIQUE,
        state INTEGER,
        FOREIGN KEY(user) REFERENCES users(id)
    )"
    sql.ExecuteNonQuery() |> ignore
    let sql = new SQLiteCommand(connection)
    sql.CommandText <- "CREATE TABLE IF NOT EXISTS images (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        uuid TEXT
    )"
    sql.ExecuteNonQuery() |> ignore
    let sql = new SQLiteCommand(connection)
    sql.CommandText <- "CREATE TABLE IF NOT EXISTS userimages (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        user INTEGER,
        image INTEGER,
        type INTEGER,
        FOREIGN KEY(user) REFERENCES users(id),
        FOREIGN KEY(image) REFERENCES images(id) ON DELETE CASCADE
    )"
    sql.ExecuteNonQuery() |> ignore

let private insertUser (db: Database) (user: FunogramUser) =
    let sql = db.CreateCommand()
    sql.CommandText <- "INSERT OR IGNORE INTO users(userid, username)
        VALUES (@userid, @username);
    SELECT id FROM users u where u.userid = @userid"

    let userIdParam = new SQLiteParameter("userid", DbType.Int64)
    userIdParam.Value <- user.Id
    sql.Parameters.Add(userIdParam) |> ignore

    let usernameParam = new SQLiteParameter("username", DbType.String)
    usernameParam.Value <- user.Username
    sql.Parameters.Add(usernameParam) |> ignore

    sql.Prepare()
    let reader = sql.ExecuteReader()
    if not reader.HasRows then
        failwith "Unable to find inserted user"
    else
        reader.Read() |> ignore
        let id = reader.GetInt64(0)
        { Id = id
          UserId = user.Id
          Username = user.Username }

let init() =
    let connection = new SQLiteConnection("Data Source=state.sqlite;Version=3;")
    connection.Open()
    createTables connection
    connection

let cleanup (db: Database) = db.Close()

let getUserState (db: Database) (userId: int64) =
    let sql = db.CreateCommand()
    sql.CommandText <- "SELECT state FROM userstates us
        JOIN users u ON us.user = u.id
        WHERE u.userid = @userid"

    let param = new SQLiteParameter("@userid", DbType.Int64)
    param.Value <- userId
    sql.Parameters.Add(param) |> ignore

    sql.Prepare()
    let result = sql.ExecuteReader()
    if not result.HasRows then
        State.Initial
    else
        result.Read() |> ignore
        result.GetInt32(0) |> enum

let setUserState (db: Database) user (state: State) =
    let user = insertUser db user

    let sql = db.CreateCommand()
    sql.CommandText <- "INSERT INTO userstates(user, state)
        VALUES (@userkey, @state)
        ON CONFLICT(user) DO UPDATE SET state = @state"

    let userKeyParam = new SQLiteParameter("@userkey", DbType.Int64)
    userKeyParam.Value <- user.Id
    sql.Parameters.Add(userKeyParam) |> ignore

    let userKeyParam = new SQLiteParameter("@state", DbType.Int32)
    userKeyParam.Value <- int state
    sql.Parameters.Add(userKeyParam) |> ignore

    sql.Prepare()
    sql.ExecuteNonQuery() |> ignore

let private getUserImages (db: Database) user =
    let sql = db.CreateCommand()
    sql.CommandText <- "SELECT uuid FROM bases b WHERE b.user = @userkey"

    let userKeyParam = new SQLiteParameter("@userkey", DbType.Int64)
    userKeyParam.Value <- user.Id
    sql.Parameters.Add(userKeyParam) |> ignore

    sql.Prepare()
    let reader = sql.ExecuteReader()
    if not reader.HasRows then
        None
    else
        reader.Read() |> ignore
        reader.GetGuid(0).ToString() |> Some

let deleteImage (db: Database) (user: FunogramUser) (imageType: ImageType) =
    let user = insertUser db user
    let sql = db.CreateCommand()
    sql.CommandText <- "SELECT uuid FROM userimages ui
        JOIN images i ON ui.image = i.id
        WHERE iu.user = @userKey
        AND ui.type = @imageType"

    let userKeyParam = SQLiteParameter("@userKey", DbType.Int64)
    userKeyParam.Value <- user.Id
    sql.Parameters.Add(userKeyParam) |> ignore

    let imageTypeParam = SQLiteParameter("@imageType", DbType.Int64)
    imageTypeParam.Value <- int64 imageType
    sql.Parameters.Add(imageTypeParam) |> ignore

    sql.Prepare()
    let reader = sql.ExecuteReader()
    while reader.HasRows do
        reader.Read() |> ignore
        reader.GetGuid(0).ToString()
        |> FileManagement.deleteGuid

    let sql = db.CreateCommand()
    sql.CommandText <- "DELETE FROM userimages ui
        WHERE ui.user = @userKey
        AND ui.type = @imageType
        "

    let userKeyParam = SQLiteParameter("@userKey", DbType.Int64)
    userKeyParam.Value <- user.Id
    sql.Parameters.Add(userKeyParam) |> ignore

    let imageTypeParam = SQLiteParameter("@imageType", DbType.Int64)
    imageTypeParam.Value <- int64 imageType
    sql.Parameters.Add(imageTypeParam) |> ignore

    sql.Prepare()
    sql.ExecuteNonQuery() |> ignore
