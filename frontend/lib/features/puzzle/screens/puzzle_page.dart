
import 'dart:async';
import 'dart:ui' show ImageFilter;

import 'package:chessground/chessground.dart';
import 'package:dartchess/dartchess.dart';
import 'package:flutter/material.dart';

import '../../../core/storage/client_id_storage.dart';
import '../models/puzzle.dart';
import '../services/puzzle_api_service.dart';

class PuzzlePage extends StatefulWidget {
  const PuzzlePage({super.key});

  @override
  State<PuzzlePage> createState() => _PuzzlePageState();
}

class _PuzzlePageState extends State<PuzzlePage> {
  static const Color _background = Color(0xFF101A17);
  static const Color _surface = Color(0xFF1B2923);
  static const Color _primary = Color(0xFFB7D7A8);
  static const Color _secondary = Color(0xFFD6B46A);

  final PuzzleApiService _puzzleApiService = PuzzleApiService();
  final ClientIdStorage _clientIdStorage = ClientIdStorage();

  bool _isLoading = true;
  bool _isSolved = false;
  bool _isSubmitting = false;
  bool _hasSubmitted = false;
  bool _isReplay = false;
  bool _usedHint = false;

  // Kullanıcı Başla butonuna bastı mı?
  bool _hasStarted = false;

  String? _errorMessage;
  String? _submissionError;
  String? _hintSquare;

  String _feedback = 'Bulmaca yükleniyor...';

  Puzzle? _puzzle;

  Position? _position;
  Position? _startingPosition;

  ChessboardController? _controller;
  Move? _lastMove;

  Timer? _timer;

  int _nextMoveIndex = 0;
  int _elapsedSeconds = 0;
  int _wrongMoves = 0;
  int? _score;

  bool get _canUseHint {
    final position = _position;
    final puzzle = _puzzle;

    return _hasStarted &&
        !_isLoading &&
        !_isSolved &&
        !_isSubmitting &&
        !_usedHint &&
        position != null &&
        puzzle != null &&
        position.turn == Side.white &&
        _nextMoveIndex < puzzle.moves.length;
  }

  @override
  void initState() {
    super.initState();
    _loadTodayPuzzle();
  }

  @override
  void dispose() {
    _timer?.cancel();
    _controller?.dispose();
    super.dispose();
  }

  // Kronometreyi başlatır.
  void _startTimer() {
    _timer?.cancel();

    _timer = Timer.periodic(
      const Duration(seconds: 1),
          (_) {
        if (!mounted || _isSolved || !_hasStarted) return;

        setState(() {
          _elapsedSeconds++;
        });
      },
    );
  }

  void _stopTimer() {
    _timer?.cancel();
    _timer = null;
  }

  // Kullanıcı Başla butonuna bastığında çağrılır.
  void _startPuzzle() {
    if (_hasStarted || _isLoading || _puzzle == null) return;

    setState(() {
      _hasStarted = true;
      _feedback = _hasSubmitted
          ? 'Tekrar oynuyorsun. Önceki puanın korunuyor.'
          : 'Sıra sende! Doğru hamleyi bul.';
    });

    // Süre yalnızca kullanıcı Başla butonuna bastığında başlar.
    _startTimer();
  }

  String _formatTime(int seconds) {
    final minutes = seconds ~/ 60;
    final remainingSeconds = seconds % 60;

    return '${minutes.toString().padLeft(2, '0')}:'
        '${remainingSeconds.toString().padLeft(2, '0')}';
  }

  Future<void> _loadTodayPuzzle() async {
    _stopTimer();

    _controller?.dispose();
    _controller = null;

    setState(() {
      _isLoading = true;
      _isSolved = false;
      _isSubmitting = false;
      _hasSubmitted = false;
      _isReplay = false;
      _usedHint = false;
      _hasStarted = false;

      _errorMessage = null;
      _submissionError = null;
      _hintSquare = null;

      _feedback = 'Bulmaca yükleniyor...';

      _puzzle = null;
      _position = null;
      _startingPosition = null;
      _lastMove = null;

      _nextMoveIndex = 0;
      _elapsedSeconds = 0;
      _wrongMoves = 0;
      _score = null;
    });

    try {
      final clientId = await _clientIdStorage.getClientId();

      final response = await _puzzleApiService.getTodayPuzzle(
        clientId: clientId,
      );

      if (!response.success || response.data == null) {
        throw Exception(
          response.message ?? 'Bulmaca bilgisi alınamadı.',
        );
      }

      final puzzle = response.data!;

      if (puzzle.moves.isEmpty) {
        throw Exception('Bulmacanın hamle listesi boş.');
      }

      final Position initialPosition = Chess.fromSetup(
        Setup.parseFen(puzzle.fen),
      );

      var validationPosition = initialPosition;

      // API'den gelen bütün hamlelerin geçerli olduğunu kontrol et.
      for (final uci in puzzle.moves) {
        final move = NormalMove.fromUci(uci);

        if (!validationPosition.isLegal(move)) {
          throw Exception('Bulmacada geçersiz hamle bulundu: $uci');
        }

        validationPosition = validationPosition.play(move);
      }

      // İlk hamle rakibin açılış hamlesidir.
      final firstMove = NormalMove.fromUci(puzzle.moves.first);

      final positionAfterOpponent = initialPosition.play(firstMove);

      if (!mounted) return;

      _puzzle = puzzle;
      _startingPosition = positionAfterOpponent;
      _position = positionAfterOpponent;
      _lastMove = firstMove;
      _nextMoveIndex = 1;

      _controller = ChessboardController(
        game: _buildGame(positionAfterOpponent),
      );

      setState(() {
        _isLoading = false;

        _hasSubmitted = puzzle.isSolved;
        _isReplay = puzzle.isSolved;
        _score = puzzle.previousScore;

        _isSolved = false;
        _hasStarted = false;

        _feedback = _hasSubmitted
            ? 'Bu bulmacayı daha önce çözdün. Tekrar oynayabilirsin.'
            : 'Hazır olduğunda bulmacaya başlayabilirsin.';
      });

      // DİKKAT:
      // Burada kronometre başlatılmıyor.
      // Kullanıcı Başla butonuna bastığında başlayacak.
    } catch (error, stackTrace) {
      debugPrint('Puzzle yükleme hatası: $error');
      debugPrint('$stackTrace');

      if (!mounted) return;

      setState(() {
        _isLoading = false;
        _errorMessage = error.toString();
      });
    }
  }

  GameData _buildGame(Position position) {
    return GameData(
      fen: position.fen,
      lastMove: _lastMove,
      playerSide: PlayerSide.white,
      sideToMove: position.turn,
      validMoves: makeLegalMoves(position),
      kingSquareInCheck: position.isCheck
          ? position.board.kingOf(position.turn)
          : null,
    );
  }

  void _onMove(Move move, {bool? viaDragAndDrop}) {
    if (!_hasStarted ||
        _isSolved ||
        _isLoading ||
        _isSubmitting) {
      return;
    }

    final position = _position;
    final puzzle = _puzzle;

    if (position == null || puzzle == null) return;

    if (!position.isLegal(move)) {
      _restoreBoard();
      return;
    }

    if (_nextMoveIndex >= puzzle.moves.length) {
      _restoreBoard();
      return;
    }

    final expectedMove = puzzle.moves[_nextMoveIndex];

    if (move.uci != expectedMove) {
      setState(() {
        _wrongMoves++;
        _feedback = 'Yanlış hamle. Tekrar dene!';
      });

      _restoreBoard();
      return;
    }

    final newPosition = position.play(move);

    _position = newPosition;
    _lastMove = move;
    _nextMoveIndex++;

    _controller?.updatePosition(
      _buildGame(newPosition),
    );

    if (_nextMoveIndex >= puzzle.moves.length) {
      _finishPuzzle();
      return;
    }

    setState(() {
      _feedback = 'Doğru hamle! Rakibin cevabı bekleniyor...';
      _hintSquare = null;
    });

    if (newPosition.turn == Side.black) {
      _playOpponentMove();
    }
  }

  Future<void> _playOpponentMove() async {
    final puzzle = _puzzle;
    final position = _position;

    if (puzzle == null || position == null) return;

    if (_nextMoveIndex >= puzzle.moves.length) {
      _finishPuzzle();
      return;
    }

    await Future.delayed(
      const Duration(milliseconds: 600),
    );

    if (!mounted || _isSolved || !_hasStarted) return;

    // Bekleme sırasında oyun yeniden başlatılmış olabilir.
    final currentPosition = _position;

    if (currentPosition == null) return;

    final uci = puzzle.moves[_nextMoveIndex];
    final move = NormalMove.fromUci(uci);

    if (!currentPosition.isLegal(move)) {
      setState(() {
        _errorMessage = 'Rakibin hamlesi geçersiz: $uci';
      });
      return;
    }

    final newPosition = currentPosition.play(move);

    _position = newPosition;
    _lastMove = move;
    _nextMoveIndex++;

    _controller?.updatePosition(
      _buildGame(newPosition),
    );

    if (_nextMoveIndex >= puzzle.moves.length) {
      _finishPuzzle();
      return;
    }

    setState(() {
      _feedback = 'Sıra sende! Doğru hamleyi bul.';
      _hintSquare = null;
    });
  }

  void _restoreBoard() {
    final position = _position;

    if (position == null) return;

    _controller?.updatePosition(
      _buildGame(position),
      animate: false,
      resetPremove: true,
    );
  }

  // Sıradaki doğru hamlenin başlangıç karesini gösterir.
  // Hedef kare ve hamlenin tamamı açıklanmaz.
  void _useHint() {
    if (!_canUseHint) return;

    final puzzle = _puzzle!;

    final expectedMove = puzzle.moves[_nextMoveIndex];

    if (expectedMove.length < 4) return;

    final fromSquare = expectedMove.substring(0, 2);

    setState(() {
      _usedHint = true;
      _hintSquare = fromSquare;
      _feedback = 'İpucu: $fromSquare karesindeki taşı hareket ettir.';
    });
  }

  Future<void> _finishPuzzle() async {
    if (!mounted || _isSolved) return;

    _stopTimer();

    setState(() {
      _isSolved = true;
      _feedback = 'Tebrikler! Bulmacayı başarıyla çözdün.';
    });

    // Daha önce kayıt yapılmışsa tekrar puan gönderme.
    if (!_hasSubmitted) {
      await _submitPuzzle();
    } else {
      setState(() {
        _feedback =
        'Bulmacayı tekrar çözdün. Önceki puanın korunuyor.';
      });
    }
  }

  int? _readScore(dynamic data) {
    if (data is! Map) return null;

    final rawScore = data['score'] ??
        data['Score'] ??
        data['totalScore'] ??
        data['TotalScore'];

    if (rawScore is num) {
      return rawScore.toInt();
    }

    if (rawScore != null) {
      return int.tryParse(rawScore.toString());
    }

    return null;
  }

  Future<void> _submitPuzzle() async {
    if (_isSubmitting || _hasSubmitted) return;

    final puzzle = _puzzle;

    if (puzzle == null) return;

    setState(() {
      _isSubmitting = true;
      _submissionError = null;
      _feedback = 'Sonuç sunucuya kaydediliyor...';
    });

    try {
      final clientId = await _clientIdStorage.getClientId();

      final response = await _puzzleApiService.submitPuzzle(
        request: {
          'clientId': clientId,
          'puzzleId': puzzle.id,
          'seconds': _elapsedSeconds,
          'wrongMoves': _wrongMoves,
          'usedHint': _usedHint,
          'countryCode': null,
        },
      );

      debugPrint('Submit API success: ${response.success}');
      debugPrint('Submit API message: ${response.message}');
      debugPrint('Submit API data: ${response.data}');

      if (!response.success) {
        throw Exception(
          response.message ?? 'Bulmaca sonucu kaydedilemedi.',
        );
      }

      final receivedScore = _readScore(response.data);

      debugPrint('Alınan puan: $receivedScore');

      if (!mounted) return;

      setState(() {
        _score = receivedScore;
        _hasSubmitted = true;
        _isReplay = false;
        _isSubmitting = false;

        _feedback = receivedScore != null
            ? 'Tebrikler! Bulmaca sonucunuz kaydedildi.'
            : 'Sonucunuz kaydedildi ancak puan bilgisi alınamadı.';
      });
    } catch (error, stackTrace) {
      debugPrint('Bulmaca gönderme hatası: $error');
      debugPrint('$stackTrace');

      if (!mounted) return;

      setState(() {
        _isSubmitting = false;
        _submissionError = error.toString();
        _feedback = 'Bulmaca çözüldü ancak sonuç kaydedilemedi.';
      });
    }
  }

  // Bulmacayı tekrar başlangıç konumuna getirir.
  // Kullanıcı yeniden Başla butonuna basmalıdır.
  void _restartPuzzle() {
    final startingPosition = _startingPosition;
    final puzzle = _puzzle;

    if (startingPosition == null ||
        puzzle == null ||
        _isSubmitting) {
      return;
    }

    _stopTimer();

    final firstMove = NormalMove.fromUci(
      puzzle.moves.first,
    );

    setState(() {
      _isSolved = false;
      _isReplay = _hasSubmitted;

      _hasStarted = false;

      _position = startingPosition;
      _lastMove = firstMove;
      _nextMoveIndex = 1;

      _elapsedSeconds = 0;
      _wrongMoves = 0;

      _usedHint = false;
      _hintSquare = null;

      _submissionError = null;

      _feedback = _hasSubmitted
          ? 'Tekrar deniyorsun. Hazır olduğunda başlayabilirsin.'
          : 'Hazır olduğunda bulmacaya başlayabilirsin.';
    });

    _controller?.updatePosition(
      _buildGame(startingPosition),
      animate: false,
      resetPremove: true,
    );

    // Kronometre burada başlatılmıyor.
    // Tekrar Başla butonuna basıldığında başlayacak.
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text(
          'Günün Bulmacası',
          style: TextStyle(fontWeight: FontWeight.bold),
        ),
        actions: [
          IconButton(
            onPressed: _isLoading || _isSubmitting
                ? null
                : _loadTodayPuzzle,
            icon: const Icon(Icons.refresh),
            tooltip: 'Bulmacayı yenile',
          ),
          const SizedBox(width: 8),
        ],
      ),
      body: _buildBody(),
    );
  }

  Widget _buildBody() {
    if (_isLoading) {
      return const Center(
        child: CircularProgressIndicator(color: _primary),
      );
    }

    if (_errorMessage != null) {
      return _buildErrorState();
    }

    if (_puzzle == null ||
        _position == null ||
        _controller == null) {
      return const Center(
        child: Text('Bulmaca bulunamadı.'),
      );
    }

    if (_isSolved) {
      return _buildResultBody();
    }

    return _buildPuzzleBody();
  }

  Widget _buildPuzzleBody() {
    return LayoutBuilder(
      builder: (context, constraints) {
        final boardSize = constraints.maxWidth - 32;

        return SizedBox(
          width: constraints.maxWidth,
          height: constraints.maxHeight,
          child: Stack(
            children: [
              // Bulmaca ekranının tamamı başlangıçta bulanık.
              Positioned.fill(
                child: IgnorePointer(
                  ignoring: !_hasStarted,
                  child: ImageFiltered(
                    imageFilter: ImageFilter.blur(
                      sigmaX: _hasStarted ? 0 : 7,
                      sigmaY: _hasStarted ? 0 : 7,
                    ),
                    child: SingleChildScrollView(
                      padding: const EdgeInsets.fromLTRB(
                        16,
                        12,
                        16,
                        28,
                      ),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          _buildPuzzleInfo(),
                          const SizedBox(height: 16),
                          _buildTimerAndMistakes(),
                          const SizedBox(height: 18),
                          Center(
                            child: SizedBox(
                              width: boardSize,
                              height: boardSize,
                              child: Chessboard(
                                controller: _controller!,
                                size: boardSize,
                                orientation: Side.white,
                                onMove: _onMove,
                              ),
                            ),
                          ),
                          const SizedBox(height: 16),
                          _buildFeedback(),
                          if (_hintSquare != null) ...[
                            const SizedBox(height: 12),
                            _buildHintCard(),
                          ],
                          const SizedBox(height: 16),
                          _buildControls(),
                          const SizedBox(height: 16),
                          _buildPuzzleStatistics(),
                        ],
                      ),
                    ),
                  ),
                ),
              ),

              // Başlangıç katmanı.
              if (!_hasStarted)
                Positioned.fill(
                  child: _buildStartOverlay(),
                ),
            ],
          ),
        );
      },
    );
  }

  Widget _buildStartOverlay() {
    return Container(
      color: _background.withValues(alpha: 0.55),
      alignment: Alignment.center,
      padding: const EdgeInsets.all(24),
      child: Container(
        width: double.infinity,
        padding: const EdgeInsets.all(24),
        decoration: BoxDecoration(
          color: _surface,
          borderRadius: BorderRadius.circular(22),
          border: Border.all(
            color: _primary.withValues(alpha: 0.25),
          ),
          boxShadow: [
            BoxShadow(
              color: Colors.black.withValues(alpha: 0.25),
              blurRadius: 24,
              offset: const Offset(0, 10),
            ),
          ],
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 68,
              height: 68,
              decoration: BoxDecoration(
                color: _primary.withValues(alpha: 0.12),
                shape: BoxShape.circle,
              ),
              child: const Icon(
                Icons.lock_open_rounded,
                color: _primary,
                size: 34,
              ),
            ),
            const SizedBox(height: 18),
            const Text(
              'Bulmaca seni bekliyor!',
              textAlign: TextAlign.center,
              style: TextStyle(
                fontSize: 21,
                fontWeight: FontWeight.bold,
              ),
            ),
            const SizedBox(height: 10),
            Text(
              _hasSubmitted
                  ? 'Bu bulmacayı daha önce çözdün. İstersen tekrar deneyebilirsin.'
                  : 'Hazır olduğunda başla. Süren butona bastığın anda başlayacak.',
              textAlign: TextAlign.center,
              style: const TextStyle(
                color: Colors.white70,
                fontSize: 13,
                height: 1.5,
              ),
            ),
            const SizedBox(height: 24),
            SizedBox(
              width: double.infinity,
              height: 54,
              child: ElevatedButton.icon(
                onPressed: _startPuzzle,
                icon: const Icon(Icons.play_arrow_rounded, size: 27),
                label: const Text(
                  'Bulmacaya Başla',
                  style: TextStyle(
                    fontSize: 16,
                    fontWeight: FontWeight.bold,
                  ),
                ),
                style: ElevatedButton.styleFrom(
                  backgroundColor: _primary,
                  foregroundColor: _background,
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(14),
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildPuzzleInfo() {
    return Container(
      padding: const EdgeInsets.all(17),
      decoration: BoxDecoration(
        color: _surface,
        borderRadius: BorderRadius.circular(18),
      ),
      child: Row(
        children: [
          Container(
            width: 48,
            height: 48,
            decoration: BoxDecoration(
              color: _primary.withValues(alpha: 0.12),
              borderRadius: BorderRadius.circular(14),
            ),
            child: const Icon(
              Icons.extension_rounded,
              color: _primary,
              size: 25,
            ),
          ),
          const SizedBox(width: 13),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text(
                  'Günün Bulmacası',
                  style: TextStyle(
                    fontSize: 16,
                    fontWeight: FontWeight.bold,
                  ),
                ),
                const SizedBox(height: 5),
                Text(
                  'Bulmaca #${_puzzle!.id}',
                  style: const TextStyle(
                    fontSize: 12,
                    color: Colors.white54,
                  ),
                ),
              ],
            ),
          ),
          Container(
            padding: const EdgeInsets.symmetric(
              horizontal: 12,
              vertical: 8,
            ),
            decoration: BoxDecoration(
              color: _secondary.withValues(alpha: 0.13),
              borderRadius: BorderRadius.circular(20),
            ),
            child: Text(
              _puzzle!.difficulty,
              style: const TextStyle(
                color: _secondary,
                fontWeight: FontWeight.bold,
                fontSize: 12,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildTimerAndMistakes() {
    return Row(
      children: [
        Expanded(
          child: _buildMiniStatistic(
            icon: Icons.timer_outlined,
            label: 'Süre',
            value: _formatTime(_elapsedSeconds),
            color: _primary,
          ),
        ),
        const SizedBox(width: 12),
        Expanded(
          child: _buildMiniStatistic(
            icon: Icons.close_rounded,
            label: 'Yanlış hamle',
            value: '$_wrongMoves',
            color: const Color(0xFFE69A91),
          ),
        ),
      ],
    );
  }

  Widget _buildMiniStatistic({
    required IconData icon,
    required String label,
    required String value,
    required Color color,
  }) {
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: 14,
        vertical: 13,
      ),
      decoration: BoxDecoration(
        color: _surface,
        borderRadius: BorderRadius.circular(14),
      ),
      child: Row(
        children: [
          Icon(icon, color: color, size: 22),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  label,
                  style: const TextStyle(
                    fontSize: 11,
                    color: Colors.white54,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  value,
                  style: const TextStyle(
                    fontSize: 17,
                    fontWeight: FontWeight.bold,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildFeedback() {
    final isWrong = _feedback.toLowerCase().contains('yanlış');
    final isSuccess = _feedback.toLowerCase().contains('doğru');

    final color = isWrong
        ? const Color(0xFFE69A91)
        : isSuccess
        ? _primary
        : _secondary;

    return Container(
      padding: const EdgeInsets.all(15),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.09),
        borderRadius: BorderRadius.circular(15),
        border: Border.all(
          color: color.withValues(alpha: 0.25),
        ),
      ),
      child: Row(
        children: [
          Icon(
            isWrong
                ? Icons.close_rounded
                : isSuccess
                ? Icons.check_circle_outline
                : Icons.info_outline,
            color: color,
            size: 23,
          ),
          const SizedBox(width: 11),
          Expanded(
            child: Text(
              _feedback,
              style: const TextStyle(
                fontSize: 13,
                height: 1.4,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildHintCard() {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: _secondary.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(15),
        border: Border.all(
          color: _secondary.withValues(alpha: 0.35),
        ),
      ),
      child: Row(
        children: [
          const Icon(
            Icons.lightbulb,
            color: _secondary,
            size: 27,
          ),
          const SizedBox(width: 12),
          const Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'İpucu',
                  style: TextStyle(
                    fontWeight: FontWeight.bold,
                    color: _secondary,
                  ),
                ),
                SizedBox(height: 4),
                Text(
                  'Doğru hamlenin başlangıç karesi:',
                  style: TextStyle(
                    fontSize: 12,
                    color: Colors.white70,
                  ),
                ),
              ],
            ),
          ),
          Container(
            padding: const EdgeInsets.symmetric(
              horizontal: 14,
              vertical: 9,
            ),
            decoration: BoxDecoration(
              color: _secondary,
              borderRadius: BorderRadius.circular(10),
            ),
            child: Text(
              _hintSquare!,
              style: const TextStyle(
                color: _background,
                fontWeight: FontWeight.bold,
                fontSize: 18,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildControls() {
    return Row(
      children: [
        Expanded(
          child: OutlinedButton.icon(
            onPressed: _isSubmitting ? null : _restartPuzzle,
            icon: const Icon(Icons.restart_alt),
            label: const Text('Yeniden Başlat'),
            style: OutlinedButton.styleFrom(
              foregroundColor: Colors.white70,
              side: const BorderSide(color: Colors.white24),
              padding: const EdgeInsets.symmetric(vertical: 14),
            ),
          ),
        ),
        const SizedBox(width: 12),
        Expanded(
          child: ElevatedButton.icon(
            onPressed: _canUseHint ? _useHint : null,
            icon: const Icon(Icons.lightbulb_outline),
            label: Text(_usedHint ? 'İpucu Kullanıldı' : 'İpucu Al'),
            style: ElevatedButton.styleFrom(
              backgroundColor: _secondary,
              foregroundColor: _background,
              padding: const EdgeInsets.symmetric(vertical: 14),
              disabledBackgroundColor: _surface,
              disabledForegroundColor: Colors.white38,
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildPuzzleStatistics() {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: _surface,
        borderRadius: BorderRadius.circular(16),
      ),
      child: Row(
        children: [
          const Icon(
            Icons.emoji_events_outlined,
            color: _secondary,
            size: 22,
          ),
          const SizedBox(width: 10),
          const Expanded(
            child: Text(
              'En yüksek puan için hızlı ve hatasız çöz!',
              style: TextStyle(
                fontSize: 12,
                color: Colors.white70,
              ),
            ),
          ),
          if (_usedHint)
            const Text(
              '-250',
              style: TextStyle(
                color: Color(0xFFE69A91),
                fontWeight: FontWeight.bold,
              ),
            ),
        ],
      ),
    );
  }

  // Bulmaca tamamlandığında gösterilen sonuç ekranı.
  Widget _buildResultBody() {
    return SingleChildScrollView(
      padding: const EdgeInsets.all(20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const SizedBox(height: 16),
          Center(
            child: Container(
              width: 104,
              height: 104,
              decoration: BoxDecoration(
                color: _secondary.withValues(alpha: 0.15),
                shape: BoxShape.circle,
              ),
              child: const Icon(
                Icons.emoji_events,
                size: 64,
                color: _secondary,
              ),
            ),
          ),
          const SizedBox(height: 24),
          Text(
            _isReplay ? 'Tekrar Tebrikler!' : 'Harika İş Çıkardın!',
            textAlign: TextAlign.center,
            style: const TextStyle(
              fontSize: 26,
              fontWeight: FontWeight.bold,
            ),
          ),
          const SizedBox(height: 10),
          Text(
            _isReplay
                ? 'Günlük bulmacayı yeniden başarıyla çözdün.'
                : 'Günün bulmacasını başarıyla tamamladın.',
            textAlign: TextAlign.center,
            style: const TextStyle(
              fontSize: 14,
              color: Colors.white70,
            ),
          ),
          const SizedBox(height: 26),
          _buildResultScoreCard(),
          const SizedBox(height: 18),
          _buildResultStatistics(),
          if (_submissionError != null) ...[
            const SizedBox(height: 16),
            _buildSubmissionError(),
          ],
          const SizedBox(height: 24),
          SizedBox(
            height: 52,
            child: ElevatedButton.icon(
              onPressed: _isSubmitting ? null : _restartPuzzle,
              icon: const Icon(Icons.replay),
              label: const Text(
                'Tekrar Çöz',
                style: TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.bold,
                ),
              ),
            ),
          ),
          const SizedBox(height: 12),
          SizedBox(
            height: 48,
            child: OutlinedButton.icon(
              onPressed: _isSubmitting ? null : _loadTodayPuzzle,
              icon: const Icon(Icons.refresh),
              label: const Text('Bulmacayı Yenile'),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildResultScoreCard() {
    return Container(
      padding: const EdgeInsets.symmetric(
        vertical: 26,
        horizontal: 16,
      ),
      decoration: BoxDecoration(
        color: _surface,
        borderRadius: BorderRadius.circular(20),
        border: Border.all(
          color: _secondary.withValues(alpha: 0.25),
        ),
      ),
      child: Column(
        children: [
          const Icon(
            Icons.stars,
            size: 36,
            color: _secondary,
          ),
          const SizedBox(height: 12),
          Text(
            _score == null ? 'Puan' : 'Puanın',
            style: const TextStyle(
              fontSize: 15,
              color: Colors.white70,
            ),
          ),
          const SizedBox(height: 8),
          Text(
            _score?.toString() ?? '--',
            style: const TextStyle(
              fontSize: 42,
              fontWeight: FontWeight.bold,
              color: _secondary,
            ),
          ),
          const SizedBox(height: 8),
          Text(
            _isReplay
                ? 'Önceki kayıtlı puanın korunuyor.'
                : _hasSubmitted
                ? 'Puanın başarıyla kaydedildi.'
                : _isSubmitting
                ? 'Puanın kaydediliyor...'
                : 'Puan henüz kaydedilmedi.',
            textAlign: TextAlign.center,
            style: const TextStyle(
              fontSize: 12,
              color: Colors.white60,
            ),
          ),
          if (_isSubmitting) ...[
            const SizedBox(height: 16),
            const CircularProgressIndicator(color: _primary),
          ],
        ],
      ),
    );
  }

  Widget _buildResultStatistics() {
    return Container(
      padding: const EdgeInsets.all(17),
      decoration: BoxDecoration(
        color: _surface,
        borderRadius: BorderRadius.circular(18),
      ),
      child: Column(
        children: [
          const Align(
            alignment: Alignment.centerLeft,
            child: Text(
              'Deneme İstatistikleri',
              style: TextStyle(
                fontSize: 16,
                fontWeight: FontWeight.bold,
              ),
            ),
          ),
          const SizedBox(height: 18),
          _buildStatisticRow(
            icon: Icons.timer_outlined,
            label: 'Çözüm süresi',
            value: _formatTime(_elapsedSeconds),
          ),
          const Divider(height: 24, color: Colors.white12),
          _buildStatisticRow(
            icon: Icons.close,
            label: 'Yanlış hamle',
            value: '$_wrongMoves',
          ),
          const Divider(height: 24, color: Colors.white12),
          _buildStatisticRow(
            icon: Icons.lightbulb_outline,
            label: 'İpucu kullanımı',
            value: _usedHint ? 'Kullanıldı' : 'Kullanılmadı',
          ),
          const Divider(height: 24, color: Colors.white12),
          _buildStatisticRow(
            icon: Icons.check_circle_outline,
            label: 'Bulmaca durumu',
            value: 'Tamamlandı',
            valueColor: _primary,
          ),
        ],
      ),
    );
  }

  Widget _buildSubmissionError() {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: Colors.red.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(15),
        border: Border.all(
          color: Colors.redAccent.withValues(alpha: 0.25),
        ),
      ),
      child: Column(
        children: [
          const Row(
            children: [
              Icon(
                Icons.error_outline,
                color: Colors.redAccent,
              ),
              SizedBox(width: 8),
              Expanded(
                child: Text(
                  'Sonuç kaydedilemedi',
                  style: TextStyle(
                    fontWeight: FontWeight.bold,
                    color: Colors.redAccent,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            _submissionError!,
            style: const TextStyle(
              color: Colors.white70,
              fontSize: 12,
            ),
          ),
          const SizedBox(height: 12),
          SizedBox(
            width: double.infinity,
            child: ElevatedButton.icon(
              onPressed: _isSubmitting ? null : _submitPuzzle,
              icon: const Icon(Icons.cloud_upload_outlined),
              label: const Text('Sonucu Tekrar Kaydet'),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildStatisticRow({
    required IconData icon,
    required String label,
    required String value,
    Color? valueColor,
  }) {
    return Row(
      children: [
        Icon(
          icon,
          color: Colors.white70,
          size: 21,
        ),
        const SizedBox(width: 10),
        Expanded(child: Text(label)),
        Text(
          value,
          style: TextStyle(
            fontWeight: FontWeight.bold,
            fontSize: 14,
            color: valueColor,
          ),
        ),
      ],
    );
  }

  Widget _buildErrorState() {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Icon(
              Icons.error_outline,
              size: 56,
              color: Color(0xFFE69A91),
            ),
            const SizedBox(height: 16),
            const Text(
              'Bulmaca yüklenemedi',
              style: TextStyle(
                fontSize: 20,
                fontWeight: FontWeight.bold,
              ),
            ),
            const SizedBox(height: 12),
            SelectableText(
              _errorMessage!,
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 20),
            ElevatedButton(
              onPressed: _loadTodayPuzzle,
              child: const Text('Tekrar Dene'),
            ),
          ],
        ),
      ),
    );
  }
}
