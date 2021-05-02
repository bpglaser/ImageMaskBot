module Tests

open System
open System.Data.SQLite

open Expecto
open FsCheck

open UserState
open FunogramHelpers

let setupEmptyDatabase() =
    let db = new SQLiteConnection("Data Source=:memory:;Version=3;")
    db.Open()
    createTables db
    db

type UserGen() =
    static member User(): Arbitrary<FunogramUser> =
        let createUser id (username: NonEmptyString) =
            { Id = id
              Username = username.Get }

        let genId = Gen.choose (0, 10000) |> Gen.map int64
        let genUsername = Arb.Default.NonEmptyString().Generator
        Arb.fromGen (createUser <!> genId <*> genUsername)

type StateGen() =
    static member State(): Arbitrary<State> = Arb.fromGen (Gen.elements (State.GetValues()))

type ImageTypeGen() =
    static member ImageType(): Arbitrary<ImageType> = Arb.fromGen (Gen.elements (ImageType.GetValues()))

let config =
    { FsCheckConfig.defaultConfig with
          arbitrary =
              [ typeof<UserGen>
                typeof<StateGen>
                typeof<ImageTypeGen> ] }

[<Tests>]
let userState =
    testList "UserState"
        [ testPropertyWithConfig config "It should default to Initial" <| fun (userId: int64) ->
            use db = setupEmptyDatabase()
            Expect.equal (getUserState db userId) State.Initial "The default should be initial"

          testPropertyWithConfig config "It should set the user state" <| fun (user: FunogramUser) (state: State) ->
              use db = setupEmptyDatabase()
              setUserState db user state
              let actualState = getUserState db user.Id
              Expect.equal actualState state "States should be equal"

          testPropertyWithConfig config "Adding a user should increase" <| fun (user: FunogramUser) ->
              use db = setupEmptyDatabase()
              let result = insertUser db user
              Expect.equal result.UserId user.Id "Should preserve the user id"
              Expect.equal result.Username user.Username "Should preserve the username"
              let allUsersLength = getUsers db |> Seq.length
              Expect.equal allUsersLength 1 "Should have been inserted" ]

let fakeDeleteGuid (s: string) = ()

[<Tests>]
let images =
    testList "Images"
        [ testPropertyWithConfig config "Inserting an image should work" <| fun (user: FunogramUser) (guid: Guid) (imageType: ImageType) ->
            let db = setupEmptyDatabase()
            let result = insertImage db user guid imageType fakeDeleteGuid
            Expect.equal result.Image.Guid guid "Guid should be equal"
            Expect.equal result.Type imageType "Image type should be equal"
            Expect.equal result.User.UserId user.Id "Userid should be equal"
            Expect.equal result.User.Username user.Username "Username should be equal"
            let allImages = getUserImages db user |> List.ofSeq

            let expectedEntries =
                [ { Id = 1L
                    User =
                        { Id = 1L
                          UserId = user.Id
                          Username = user.Username }
                    Image =
                        { Id = 1L
                          Guid = guid }
                    Type = imageType } ]
            Expect.equal allImages expectedEntries "There should be one entry"

          testPropertyWithConfig config "Inserting multiple images for the same user should work" <| fun (user: FunogramUser) (values: (Guid * ImageType) list) ->
              let db = setupEmptyDatabase()
              for (guid, imageType) in values do
                  insertImage db user guid imageType fakeDeleteGuid |> ignore
              let allImages =
                  getUserImages db user
                  |> List.ofSeq
                  |> List.groupBy (fun ui -> ui.Type)
              Expect.isLessThanOrEqual allImages.Length (Enum.GetValues(typeof<ImageType>).Length)
                  "There should only be entries for the valid ImageTypes"
              let foundMultipleEntries =
                  allImages
                  |> Seq.map snd
                  |> Seq.map List.length
                  |> Seq.tryFind (fun len -> len > 1)
              Expect.isNone foundMultipleEntries "There should only be one image of each kind"

          testPropertyWithConfig config "Deleting an image should only trigger callback when it exists" <| fun (user: FunogramUser) (guid: Guid) (imageType: ImageType) ->
              let db = setupEmptyDatabase()
              let mutable calls = []
              let storeGuid guid = calls <- guid :: calls

              deleteImage db user imageType storeGuid
              Expect.isEmpty calls "Delete guid callback shouldn't have been called"

              insertImage db user guid imageType storeGuid |> ignore
              Expect.isEmpty calls "Delete guid callback shouldn't have been called"

              deleteImage db user imageType storeGuid
              Expect.equal calls [ guid.ToString() ] "Callback should have been triggered" ]
