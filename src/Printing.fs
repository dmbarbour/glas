namespace Glas

module Printing =
    open Effects
    type Binary = byte[]

    type PrintEff =
        val private Writer : Binary -> unit
        val mutable private TXStack : FTList<Binary> list
        new(writer) =
            { Writer = writer; TXStack = [] }

        member p.Write(b) =
            if Array.isEmpty b then () else
            match p.TXStack with
            | [] -> p.Writer b
            | (tx::txs) ->
                p.TXStack <- (FTList.snoc tx b)::txs

        member p.PushTX () =
            p.TXStack <- (FTList.empty) :: p.TXStack
        
        member p.PopTX bCommit =
            match p.TXStack with
            | [] -> invalidOp "popped empty TX stack"
            | (tx::txs) ->
                p.TXStack <- txs
                if bCommit then
                    for b in FTList.toSeq tx do
                        p.Writer b

        interface IEffHandler with
            member p.Eff vEff =
                match vEff with
                | Value.Variant "write" (Value.Binary b) -> 
                    p.Write b
                    Some Value.unit
                | _ -> None

        interface ITransactional with
            member p.Try () = p.PushTX()
            member p.Abort() = p.PopTX false
            member p.Commit() = p.PopTX true

    let printStdout () : IEffHandler =
        let stdout = System.Console.OpenStandardOutput()
        let w (b : Binary) = stdout.Write(b, 0, b.Length)
        PrintEff(w) :> IEffHandler
