
using ChessChallenge.API;
using System;
using System.Linq;
    public class MyBot : IChessBot
    {


        
        // Tyrants PSTS,
        // Could quantize my own, but i think for now this is good enough
        private readonly short[] PieceValues = { 82, 337, 365, 477, 1025, 0, // Middlegame
                                                94, 281, 297, 512, 936, 0}; // Endgame      

        private readonly int[][] UnpackedPestoTables;   

        public MyBot()
        {
            UnpackedPestoTables = new[] {  63746705523041458768562654720m, 71818693703096985528394040064m, 75532537544690978830456252672m, 75536154932036771593352371712m, 76774085526445040292133284352m, 3110608541636285947269332480m, 936945638387574698250991104m, 75531285965747665584902616832m,77047302762000299964198997571m, 3730792265775293618620982364m, 3121489077029470166123295018m, 3747712412930601838683035969m, 3763381335243474116535455791m, 8067176012614548496052660822m, 4977175895537975520060507415m, 2475894077091727551177487608m,2458978764687427073924784380m, 3718684080556872886692423941m, 4959037324412353051075877138m, 3135972447545098299460234261m, 4371494653131335197311645996m, 9624249097030609585804826662m, 9301461106541282841985626641m, 2793818196182115168911564530m, 77683174186957799541255830262m, 4660418590176711545920359433m, 4971145620211324499469864196m, 5608211711321183125202150414m, 5617883191736004891949734160m, 7150801075091790966455611144m, 5619082524459738931006868492m, 649197923531967450704711664m,  75809334407291469990832437230m, 78322691297526401047122740223m, 4348529951871323093202439165m, 4990460191572192980035045640m, 5597312470813537077508379404m, 4980755617409140165251173636m, 1890741055734852330174483975m, 76772801025035254361275759599m,75502243563200070682362835182m, 78896921543467230670583692029m, 2489164206166677455700101373m, 4338830174078735659125311481m, 4960199192571758553533648130m, 3420013420025511569771334658m, 1557077491473974933188251927m, 77376040767919248347203368440m,  73949978050619586491881614568m, 77043619187199676893167803647m, 1212557245150259869494540530m, 3081561358716686153294085872m, 3392217589357453836837847030m, 1219782446916489227407330320m, 78580145051212187267589731866m, 75798434925965430405537592305m,68369566912511282590874449920m, 72396532057599326246617936384m, 75186737388538008131054524416m, 77027917484951889231108827392m, 73655004947793353634062267392m, 76417372019396591550492896512m, 74568981255592060493492515584m, 70529879645288096380279255040m}
            .Select(packedTable =>
            {
                int pieceType = 0;
                return new System.Numerics.BigInteger(packedTable).ToByteArray().Take(12)
                        .Select(square => (int)((sbyte)square * 1.461) + PieceValues[pieceType++])
                    .ToArray();
            }).ToArray();
        }   

        Move rootBestMove;
        // Piece values in order of - NULL, PAWN, BISHOP , KNIGHT, ROOK, QUEEN, KING    
        Move[] kMoves = new Move[1024];

        // const int TTlength= 0x400000;
        (ulong, Move, int, int, int)[] TTtable= new (ulong, Move, int, int, int)[0x400000];
        
            public Move Think(Board board, Timer timer)
            {
                

                // Clear history
                var hMoves = new int[2, 7, 64];

                // Eval function
                int staticEvalPos()
                {
                    int middlegame = 0, endgame = 0, gamephase = 0, sideToMove = 2, piece, square;
                    for (; --sideToMove >= 0; middlegame *= -1, endgame *= -1   )
                        for (piece = -1; ++piece < 6;)
                            for (ulong mask = board.GetPieceBitboard((PieceType)piece + 1, sideToMove > 0); mask != 0;)
                            {
                                
                                // Gamephase, middlegame -> endgame
                                gamephase += 0x00042110 >> piece * 4 & 0x0F;

                                // Material and square evaluation
                                square = BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^ 56 * sideToMove;
                                middlegame += UnpackedPestoTables[square][piece];   
                                endgame += UnpackedPestoTables[square][piece + 6];
                            }
                                                                                                                    // Tempo bonus to help with aspiration windows
                    return (middlegame * gamephase + endgame * (24 - gamephase)) /  (board.IsWhiteToMove ? 24 : -24) + gamephase / 2;
                }

                // Search function
                int Negamax(int depth, int alpha, int beta, int ply, bool notLastMoveNull) {

                    bool isNotRoot = ply++ > 0, InCheck = board.IsInCheck(), notPvNode = alpha + 1 == beta, fprune = false;
                    ulong key = board.ZobristKey;

                    // Check for repetition
                    if(isNotRoot && board.IsRepeatedPosition()) return 0;   

                    int score, bestScore = -6000001;
                    // Local function 
                    int Search(int nextAlpha, int Reduction = 1, bool canNull = true) => score = -Negamax(depth - Reduction, -nextAlpha, -alpha, ply, canNull);

                    // Check Extensions
                    if(InCheck) depth++;

                    var (ttKey, ttMove, ttDepth, ttScore, ttBound) = TTtable[key % 0x400000];

                    // TT cutoff
                    if (ttKey == key) {
                        if( notPvNode && ttDepth >= depth && (
                            ttBound == 3 // exact score
                                || ttBound == 2 && ttScore >= beta // lower bound, fail high
                                || ttBound == 1 && ttScore <= alpha // upper bound, fail low
                        )) return ttScore;
                    }

                    // Internal Iterative Reductions (IIR)
                    else if(depth > 4) depth--;

                    bool qsearch = depth <= 0;
                    if(qsearch) {
                        bestScore = staticEvalPos();
                        if(bestScore >= beta) return bestScore; 
                        alpha = Math.Max(alpha, bestScore);
                    } else if (!InCheck && notPvNode) {
                        if (depth <= 8) {
                            int staticEval = staticEvalPos();
                            if (staticEval - 100 * depth >= beta) return staticEval - 100 * depth;
                            fprune = staticEval + 140 * depth <= alpha;     
                        }
    
                        if (notLastMoveNull && depth >= 2) {
                            board.TrySkipTurn();
                            Search(beta, 3 + depth / 5, false);
                            board.UndoSkipTurn();
                            if (score > beta) return score;
                        }
                    }
                    
                    var allMoves = board.GetLegalMoves(qsearch);
                    int flag = 1, i = 0;
                    var scores = new int[allMoves.Length];
            
                    // Move ordering       
                    for(; i <  allMoves.Length;) {
                        Move move = allMoves[i];
                        scores[i++] = -(ttMove == move && ttKey == key ? 1000000000 :
                                        move.IsCapture ?  100000 * (int)move.CapturePieceType - (int)move.MovePieceType :
                                        move.IsPromotion ? 100000 * (int)move.PromotionPieceType :  
                                        kMoves[ply] == move ? 95000 :
                                        hMoves[ply & 1, (int)move.MovePieceType, move.TargetSquare.Index]);
                    }

                    Move bestMove = ttMove; 
                    Array.Sort(scores, allMoves);
                    i = -1;
                    
                    // Tree search
                    foreach(Move move in allMoves) {
                        i++;

                        if (timer.MillisecondsElapsedThisTurn * 30 >= timer.MillisecondsRemaining) depth /= 0;

                        // Futility pruning
                        if (fprune && i != 0 && scores[i] > -100000) continue;

                        board.MakeMove(move);
                            // PVS + LMR
                            bool canReduce = i > 3 && depth > 3;
                            if (i == 0 || qsearch ||
                            // If PV-node / qsearch, search(beta)
                            Search(alpha + 1 , canReduce ? notPvNode ? 3 : 2 : 1) < 999999 && score > alpha && (score < beta || canReduce)
                            // If null-window search fails-high, search(beta)
                            ) Search(beta);   
                        board.UndoMove(move);


                        // Update best move if neccesary
                        if (score > bestScore) {
                            bestScore = score;
                            if (!isNotRoot) rootBestMove = move;
                            // Only update bestMove on alpha improvements   
                            if (bestScore > alpha) {
                                alpha = bestScore;
                                bestMove = move;
                                flag = 3;
                            }
                        }
                        // Fail-High condition
                        if (alpha >= beta) {
                            if (!move.IsCapture) {
                                kMoves[ply] = move;
                                hMoves[ply & 1, (int)move.MovePieceType, move.TargetSquare.Index] += depth * depth;
                            }
                            flag = 2;
                            break;
                        }


                    } // End of tree search

                    // Check stale or checkmate
                    // If bestScore hasnt been updated, we cant be in qsearch, because then bestScore is staticEval
                    // and there must be no legal moves to update bestScore
                    // therefore, since there are no legal moves, we are in check/stalemate.
                    if(bestScore == -6000001) 
                        return InCheck ? ply - 30000 : 0;

                    // Check fail-high/low/exact score for TT
                    // int bound = bestScore >= beta ? 2 : bestScore > origAlpha ? 3 : 1;

                    TTtable[key % 0x400000] = (key,
                                            bestMove, 
                                            depth, 
                                            bestScore, 
                                            flag);

                    return bestScore;

            }
            #if DEBUG //#DEBUG
                int globalEval = 0, globalDepth = 0; // #DEBUG 
            #endif //#DEBUG

            try {
                for (int depth = 0, alpha = -600000, beta = 600000;;) 
                {
                    // Aspiration windows

                    int eval = Negamax(depth, alpha, beta, 0, true);
                    
                    if (eval <= alpha) alpha -= 62;
                    else if (eval >= beta) beta += 62;
                    else {
                        alpha = eval - 17;
                        beta = eval + 17;
                        depth++;

                    #if DEBUG //#DEBUG
                        globalEval = eval;// #DEBUG 
                        globalDepth = depth; //#DEBUG
                    #endif //#DEBUG

                    }
                }
            }
            catch {  }

            #if DEBUG //#DEBUG
                Console.WriteLine("Evaluation : {0} || Time : {1} || Depth : {2}" , //#DEBUG
                                globalEval, //#DEBUG
                                timer.MillisecondsElapsedThisTurn, //#DEBUG
                                globalDepth); // #DEBUG
            #endif //#DEBUG
            return rootBestMove;
        }
    }      
