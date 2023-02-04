namespace Glas

module Stats = 

    type S =
        {
            Cnt : uint64
            Sum : float
            SSQ : float
            Min : float
            Max : float
        }

    let inline private sq x = (x * x)

    let s0 = 
        { Cnt = 0UL
        ; Sum = 0.0
        ; SSQ = 0.0
        ; Min = System.Double.PositiveInfinity
        ; Max = System.Double.NegativeInfinity
        }

    let s1 f = 
        { Cnt = 1UL
        ; Sum = f
        ; SSQ = sq f 
        ; Min = f
        ; Max = f
        }

    let add s f =
        { Cnt = 1UL + s.Cnt
        ; Sum = s.Sum + f
        ; SSQ = s.SSQ + sq f
        ; Min = min (s.Min) f
        ; Max = max (s.Max) f
        }

    let join a b = 
        { Cnt = (a.Cnt + b.Cnt)
        ; Sum = (a.Sum + b.Sum)
        ; SSQ = (a.SSQ + b.SSQ)
        ; Min = min (a.Min) (b.Min)
        ; Max = max (a.Max) (b.Max)
        }

    let average s =
        assert(s.Cnt > 0UL)
        (s.Sum / float(s.Cnt))

    let variability s =
        // (SSQ - N*(AVG^2)) / (N - 1)
        // N*(Avg^2) = N*((Sum/N)^2) = N*Sum^2 / N^2 = Sum^2 / N 
        // (SSQ - (Sum^2/N)) / (N - 1)
        assert(s.Cnt > 1UL)
        (s.SSQ - ((sq s.Sum) / float(s.Cnt))) / float(s.Cnt - 1UL)

    let sdev s = 
        sqrt (variability s)

    // I'm thinking about keeping some extra detail on stats
    // via MultiStats. In this case, we'll aggregate a set of
    // stat 'buckets' then occasionally reduce the set to keep
    // memory use under control. This would make it easier to
    // produce bar graphs of some form.
    //
    // However, it also adds a lot of overhead when profiling.
    // 

    [<Struct>]
    type MultiStat = 
        { BCT : int
        ; BS  : S list 
        }

    let private heuristicJoinCost a b =
        // weighted cost of moving each box to joined value.
        let joinAvg = (a.Sum + b.Sum) / float(a.Cnt + b.Cnt)
        let joinMax = max (a.Max) (b.Max)
        let joinMin = min (a.Min) (b.Min)
        let inline dist s =
            abs(joinAvg - (average s)) +
            (joinMax - s.Max) +
            (s.Min - joinMin)
        let inline moveCost s = 
            float(s.Cnt) * (dist s)
        moveCost a + moveCost b 

    // will reduce every sequence of 5 items to 4 items.
    let rec private reduceFifths acc rs =
        match rs with 
        | (r1::r2::r3::r4::r5::rs') -> // 5 or more items, reduce to 4 items
            let c12 = heuristicJoinCost r1 r2
            let c23 = heuristicJoinCost r2 r3
            let c34 = heuristicJoinCost r3 r4
            let c45 = heuristicJoinCost r4 r5
            let cMin = min (min c12 c23) (min c34 c45)
            let acc' =
                if (cMin = c12) then (r5::r4::r3::(join r2 r1)::acc) else
                if (cMin = c23) then (r5::r4::(join r3 r2)::r1::acc) else
                if (cMin = c34) then (r5::(join r4 r3)::r2::r1::acc) else
                assert(cMin = c45);  ((join r5 r4)::r3::r2::r1::acc)
            reduceFifths acc' rs'
        | (r::rs') -> // fewer than 5 items, is not reduced
            reduceFifths (r::acc) rs'
        | [] -> acc // preserve reverse order.

    // heuristic exponential reduction of stats buckets.
    // This reduces ~36% of items (9 per 25).
    let reduce l =
        l |> List.filter (fun s -> (s.Cnt > 0UL))
          // sort so items with similar averages are merged.
          |> List.sortBy average
          // use two 5=>4 reduction passes to avoid aliasing.
          |> reduceFifths []
          |> reduceFifths []

    // reduce to a specific number of buckets
    let rec reduceTo n l =
        if (List.length l > n) 
          then reduceTo n (reduce l)
          else l

    let autoReduce ms =
        if (100 > ms.BCT) then ms else
        let bs' = reduce ms.BS
        { BS = bs'
        ; BCT = List.length bs'
        }

    let addVal ms v =
        autoReduce { BCT = 1 + (ms.BCT); BS = (s1 v)::(ms.BS) }
