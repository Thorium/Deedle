/// Tools for generating nice member names that follow F# & .NET naming conventions
module FSharp.Data.Runtime.NameUtils

open System
open System.Globalization
open System.Collections.Generic
open FSharp.Data.Runtime

// --------------------------------------------------------------------------------------
// Active patterns & operators for parsing strings

// Todo: Convert to ValueTuple and [<return: Struct>] when F# 6 is available
let private tryAt (s:string) i = if i >= s.Length then None else Some s.[i]
let private sat f (c:option<char>) = match c with Some c when f c -> Some c | _ -> None
let private (|EOF|_|) c = match c with Some _ -> None | _ -> Some ()
let private (|LetterDigit|_|) = sat Char.IsLetterOrDigit
let private (|Upper|_|) = sat (fun c -> Char.IsUpper c || Char.IsDigit c)
let private (|Lower|_|) = sat (fun c -> Char.IsLower c || Char.IsDigit c)

// --------------------------------------------------------------------------------------

let inline internal forall predicate (source : ReadOnlySpan<_>) =
    let mutable state = true
    let mutable e = source.GetEnumerator()
    while state && e.MoveNext() do
        state <- predicate e.Current
    state

/// Turns a given non-empty string into a nice 'PascalCase' identifier
let nicePascalName (s:string) =
  if s.Length = 1 then s.ToUpperInvariant() else
  // Starting to parse a new segment
  let rec restart i =
    match tryAt s i with
    | EOF -> Seq.empty
    | LetterDigit _ & Upper _ -> upperStart i (i + 1)
    | LetterDigit _ -> consume i false (i + 1)
    | _ -> restart (i + 1)
  // Parsed first upper case letter, continue either all lower or all upper
  and upperStart from i =
    match tryAt s i with
    | Upper _ -> consume from true (i + 1)
    | Lower _ -> consume from false (i + 1)
    | _ ->
        let r1 = struct (from, i)
        let r2 = restart (i + 1)
        seq {
          yield r1
          yield! r2
        }
  // Consume are letters of the same kind (either all lower or all upper)
  and consume from takeUpper i = 
    match takeUpper, tryAt s i with
    | false, Lower _ -> consume from takeUpper (i + 1)
    | true, Upper _ -> consume from takeUpper (i + 1)
    | true, Lower _ ->
        let r1 = struct (from, (i - 1))
        let r2 = restart (i - 1)
        seq {
          yield r1
          yield! r2
        }
    | _ ->
        let r1 = struct(from, i)
        let r2 = restart i
        seq {
          yield r1
          yield! r2 }

  // Split string into segments and turn them to PascalCase
  seq { for i1, i2 in restart 0 do
          let sub = s.AsSpan(i1, i2 - i1)
          if forall Char.IsLetterOrDigit sub then
            yield Char.ToUpperInvariant(sub.[0]).ToString() + sub.Slice(1).ToString().ToLowerInvariant() }
  |> String.Concat

/// Turns a given non-empty string into a nice 'camelCase' identifier
let niceCamelName (s:string) =
  let name = nicePascalName s
  if name.Length > 0 then
    name.[0].ToString().ToLowerInvariant() + name.Substring(1)
  else name

/// Given a function to format names (such as `niceCamelName` or `nicePascalName`)
/// returns a name generator that never returns duplicate name (by appending an
/// index to already used names)
///
/// This function is curried and should be used with partial function application:
///
///     let makeUnique = uniqueGenerator nicePascalName
///     let n1 = makeUnique "sample-name"
///     let n2 = makeUnique "sample-name"
///
let uniqueGenerator (niceName:string->string) =
  let set = new HashSet<_>()
  fun name ->
    let mutable name = niceName name
    if name.Length = 0 then name <- "Unnamed"
    while set.Contains name do
      let mutable lastLetterPos = String.length name - 1
      while Char.IsDigit name.[lastLetterPos] && lastLetterPos > 0 do
        lastLetterPos <- lastLetterPos - 1
      if lastLetterPos = name.Length - 1 then
        if name.Contains " " then
            name <- name + " 2"
        else
            name <- name + "2"
      elif lastLetterPos = 0 && name.Length = 1 then
        name <- (UInt64.Parse name + 1UL).ToString()
      else
        let number = name.Substring(lastLetterPos + 1)
        name <- name.Substring(0, lastLetterPos + 1) + (UInt64.Parse number + 1UL).ToString()
    set.Add name |> ignore
    name

let capitalizeFirstLetter (s:string) =
    match s.Length with
        | 0 -> ""
        | 1 -> (Char.ToUpperInvariant s.[0]).ToString()
        | _ -> (Char.ToUpperInvariant s.[0]).ToString() + s.Substring(1)

/// Trim HTML tags from a given string and replace all of them with spaces
/// Multiple tags are replaced with just a single space. (This is a recursive
/// implementation that is somewhat faster than regular expression.)
let trimHtml (s:string) =
  let chars = s.ToCharArray()
  let res = new Text.StringBuilder()

  // Loop and keep track of whether we're inside a tag or not
  let rec loop i emitSpace inside =
    if i >= chars.Length then () else
    let c = chars.[i]
    match inside, c with
    | true, '>' -> loop (i + 1) false false
    | false, '<' ->
        if emitSpace then res.Append(' ') |> ignore
        loop (i + 1) false true
    | _ ->
        if not inside then res.Append(c) |> ignore
        loop (i + 1) true inside

  loop 0 false false
  res.ToString().TrimEnd()

/// Return the plural of an English word
let pluralize s =
  Pluralizer.toPlural s

/// Return the singular of an English word
let singularize s =
  Pluralizer.toSingular s
