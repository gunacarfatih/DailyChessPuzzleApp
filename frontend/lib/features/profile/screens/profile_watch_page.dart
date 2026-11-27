import 'package:chessground/chessground.dart';
import 'package:dartchess/dartchess.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../providers/profile_provider.dart';

class ProfileWatchPage extends StatefulWidget {
  const ProfileWatchPage({super.key, required this.attemptId});
  final int attemptId;
  @override
  State<ProfileWatchPage> createState() => _ProfileWatchPageState();
}

class _ProfileWatchPageState extends State<ProfileWatchPage> {
  Map<String, dynamic>? _game;
  String? _error;
  List<String> _moves = const [];
  int _index = 0;
  ChessboardController? _board;
  Position? _position;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final data = await context.read<ProfileProvider>().watch(
        widget.attemptId,
      );
      _game = data;
      _moves = (data['moves']?.toString() ?? '')
          .split(RegExp(r'\s+'))
          .where((m) => m.isNotEmpty)
          .toList();
      _position = Chess.fromSetup(Setup.parseFen(data['fen'].toString()));
      _syncBoard();
      if (mounted) setState(() {});
    } catch (e) {
      if (mounted) setState(() => _error = 'Çözüm yüklenemedi.');
    }
  }

  void _syncBoard() {
    final position = _position!;
    _board?.dispose();
    _board = ChessboardController(
      game: GameData(
        fen: position.fen,
        lastMove: _index == 0 ? null : NormalMove.fromUci(_moves[_index - 1]),
        playerSide: PlayerSide.white,
        sideToMove: position.turn,
        validMoves: makeLegalMoves(position),
        kingSquareInCheck: position.isCheck
            ? position.board.kingOf(position.turn)
            : null,
      ),
    );
  }

  void _step(int delta) {
    final next = (_index + delta).clamp(0, _moves.length).toInt();
    if (next == _index) return;
    var position = Chess.fromSetup(Setup.parseFen(_game!['fen'].toString()));
    for (final uci in _moves.take(next)) {
      final move = NormalMove.fromUci(uci);
      if (!position.isLegal(move)) {
        setState(() => _error = 'Bulmaca hamle dizisi geçersiz.');
        return;
      }
      position = position.play(move);
    }
    _position = position;
    _index = next;
    _syncBoard();
    setState(() {});
  }

  @override
  void dispose() {
    _board?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final data = _game;
    return Scaffold(
      appBar: AppBar(title: const Text('Çözümü İzle')),
      body: _error != null
          ? Center(child: Text(_error!))
          : data == null || _board == null
          ? const Center(child: CircularProgressIndicator())
          : Column(
              children: [
                const SizedBox(height: 16),
                Text(
                  'Rating ${data['rating']} · ${data['themes']}',
                  style: const TextStyle(color: Color(0xFFD4AF37)),
                ),
                const SizedBox(height: 12),
                Expanded(
                  child: Center(
                    child: AspectRatio(
                      aspectRatio: 1,
                      child: AbsorbPointer(
                        child: Chessboard(
                          controller: _board!,
                          size: MediaQuery.sizeOf(context).width - 24,
                          orientation: Side.white,
                          onMove: (_) {},
                        ),
                      ),
                    ),
                  ),
                ),
                Text(
                  '$_index / ${_moves.length} hamle',
                  style: const TextStyle(color: Colors.white70),
                ),
                Row(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    IconButton(
                      onPressed: _index == 0 ? null : () => _step(-1),
                      icon: const Icon(Icons.skip_previous),
                      color: const Color(0xFFD4AF37),
                    ),
                    IconButton(
                      onPressed: _index == _moves.length
                          ? null
                          : () => _step(1),
                      icon: const Icon(Icons.skip_next),
                      color: const Color(0xFFD4AF37),
                    ),
                    TextButton(
                      onPressed: () {
                        _index = 0;
                        _position = Chess.fromSetup(
                          Setup.parseFen(data['fen'].toString()),
                        );
                        _syncBoard();
                        setState(() {});
                      },
                      child: const Text(
                        'Baştan oynat',
                        style: TextStyle(color: Color(0xFFE8C85A)),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 20),
              ],
            ),
    );
  }
}
