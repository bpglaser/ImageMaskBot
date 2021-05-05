module ImageMaskBot

open System
open System.Net

open Config
open UserState

open Funogram.Types
open Funogram.Telegram.Bot
open Funogram.Telegram.Types
open Funogram.Api
open Funogram.Telegram.Api

open SixLabors.ImageSharp
open System.Net.Http

type Context =
    { Config: BotConfig
      Base: Image
      Mask: Image
      Extents: int * int * int * int
      Dimensions: int * int
      Database: Database }

type BotError =
    | ChatIdNotFound
    | DownloadError
    | FileIdNotFound
    | BotError of ApiResponseError

[<Literal>]
let helpMessage = "this is the help message"

[<Literal>]
let aboutMessage = "this is the about message"

let sendImage ctx (update: UpdateContext) (bytes: byte array) =
    async {
        use stream = new IO.MemoryStream(bytes)
        let file = FileToSend.File("response.png", stream)
        let id = update.Update.Message |> Option.map (fun m -> m.Chat.Id)
        match id with
        | None -> return Error ChatIdNotFound
        | Some id ->
            let! result = sendPhoto id file "" |> api (ctx.Config)
            return result |> Result.mapError BotError
    }

let download (client: HttpClient) (address: string) =
    let bytes = client.GetByteArrayAsync(address) |> Async.AwaitTask |> Async.RunSynchronously
    let fileName = address.Split('/') |> Array.last
    IO.File.WriteAllBytes(fileName, bytes)
    bytes

let handlePhotoFile ctx update (bytes: byte array) =
    bytes
    |> Image.Load
    |> ImageMasking.cropToDimensions ctx.Dimensions
    |> ImageMasking.moveToPosition (ctx.Base.Width, ctx.Base.Height) ctx.Extents
    |> ImageMasking.maskImage ctx.Mask
    |> ImageMasking.stackImage ctx.Base
    |> ImageMasking.writeImageToMemory
    |> fun bytes ->
        async {
            let! bytes = bytes
            return! sendImage ctx update bytes }

let handlePhotos ctx update client (photo: PhotoSize) =
    photo
    |> FunogramHelpers.getPhotoBytes ctx.Config client
    |> Async.RunSynchronously
    |> Result.mapError BotError
    |> Result.bind (handlePhotoFile ctx update >> Async.RunSynchronously)

let photoMessage f (update: UpdateContext) =
    match update.Update.Message |> Option.bind (fun m -> m.Photo) with
    | Some photos when not (Seq.isEmpty photos) ->
        photos |> Seq.maxBy (fun p -> p.Width * p.Height) |> f |> function Ok _ -> true | _ -> false
    | _ -> 
        true

let sendBasePrompt ctx update =
    let message = "Please upload a base"
    sendMessage update.Update.Message.Value.Chat.Id message
    |> api ctx.Config
    |> Async.RunSynchronously
    |> ignore

let getPhotoBytes ctx client f (photo: PhotoSize) =
    let result = 
        photo.FileId
        |> getFile
        |> api ctx.Config
        |> Async.RunSynchronously
        |> Result.mapError BotError
    match result with
    | Ok file ->
        file.FilePath
        |> Option.map (sprintf "https://api.telegram.org/file/bot%s/%s" ctx.Config.Token)
        |> Option.map (download client)
        |> Option.iter f
        |> ignore
    | Error _ -> ()
    result

let resetBot ctx user =
    deleteImage ctx.Database user ImageType.Base FileManagement.deleteGuid
    deleteImage ctx.Database user ImageType.Mask FileManagement.deleteGuid

let updateArrived ctx client (update: UpdateContext) =
    match FunogramHelpers.getUser update with
    | None -> ()
    | Some user ->
        let state = getUserState ctx.Database user.Id
        match state with
        | State.Initial ->
            sendBasePrompt ctx update
            setUserState ctx.Database user State.PromptedForBase
        | State.PromptedForBase ->
            let onPhotoBytes bytes =
                setUserState ctx.Database user State.BaseSet
            photoMessage (getPhotoBytes ctx client onPhotoBytes) update |> ignore
        | _ -> ()
        processCommands update [
            photoMessage (handlePhotos ctx update client)
            cmd "/help" (fun _ ->
                  sendMessage update.Update.Message.Value.Chat.Id helpMessage 
                  |> api ctx.Config
                  |> Async.RunSynchronously
                  |> ignore)
            cmd "/start" (fun _ ->
                  sendMessage update.Update.Message.Value.Chat.Id aboutMessage
                  |> api ctx.Config
                  |> Async.RunSynchronously
                  |> ignore)
        ] |> ignore

let run (db: Database) (config: TelegramConfig) =
    async {
        use client = new HttpClient()
        let config = { defaultConfig with Token = config.Token }
        let! ``base`` = ImageMasking.loadImage "base.png"
        let! mask = ImageMasking.loadImage "mask.png"
        let extents = ImageMasking.findMaskExtents mask
        let dimensions = ImageMasking.extentsToDimensions extents

        let ctx =
            { Config = config
              Base = ``base``
              Mask = mask
              Extents = extents
              Dimensions = dimensions
              Database = db }
        printfn "%A" ctx
        do! startBot config (updateArrived ctx client) None
    }
