import 'dart:async';
import 'dart:ui' show ImageFilter;

import 'package:chessground/chessground.dart';
import 'package:dartchess/dartchess.dart';
import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart';

import 'package:provider/provider.dart';
import '../../../providers/auth_provider.dart';
import '../../../providers/leaderboard_provider.dart';

import '../../../core/storage/client_id_storage.dart';
import '../models/puzzle.dart';
import '../models/bonus_puzzle.dart';
import '../models/puzzle_action_result.dart';
import '../services/puzzle_api_service.dart';

class PuzzlePage extends StatefulWidget {
  const PuzzlePage({
    super.key,
    this.puzzleApiService,
    this.clientIdStorage,
  });

  final PuzzleApiService? puzzleApiService;
  final ClientIdStorage? clientIdStorage;

  @override
  State<PuzzlePage> createState() => PuzzlePageState();
}

class PuzzlePageState extends State<PuzzlePage> {
  static const Color _background = Color(0xFF101A17);
  static const Color _surface = Color(0xFF1B2923);
  static const Color _primary = Color(0xFFB7D7A8);
  static const Color _secondary = Color(0xFFD6B46A);
  static const int _bonusDurationSeconds = 300;
  static const _opponentReplyDelay = Duration(milliseconds: 600);
  static const _opponentAnimationDelay = Duration(milliseconds: 300);

  late final PuzzleApiService _puzzleApiService =
      widget.puzzleApiService ?? PuzzleApiService();
  late final ClientIdStorage _clientIdStorage =
      widget.clientIdStorage ?? ClientIdStorage();

  // Hesap veya bulmaca oturumu değiştiğinde artırılır.
  // Önceki oturumlardan dönen asenkron cevaplar böylece geçersiz olur.
  int _puzzleGeneration = 0;

  bool _isCurrentGeneration(int generation) {
    return mounted && generation == _puzzleGeneration;
  }

  // Günlük bulmaca
  bool _isLoading = true;
  bool _isSolved = false;
  bool _isSubmitting = false;
  bool _hasSubmitted = false;
  bool _isReplay = false;
  bool _usedHint = false;
  bool _hasStarted = false;
  bool _isAuthenticated = false;
  bool _showCompletionAnimation = false;

  String? _errorMessage;
  String? _submissionError;
  String? _hintSquare;
  String _feedback = 'Bulmaca yükleniyor...';

  Puzzle? _puzzle;

  Position? _position;
  Position? _startingPosition;
  ChessboardController? _controller;
  Move? _lastMove;
  String? _wrongMoveUci;
  Timer? _wrongMoveTimer;
  static const _wrongMoveDuration = Duration(milliseconds: 800);
  static const _wrongMoveColor = Color(0xFFD32F2F);

  Timer? _timer;
  final Stopwatch _dailyElapsedClock = Stopwatch();
  int _dailyElapsedAtSync = 0;

  int _nextMoveIndex = 0;
  int _elapsedSeconds = 0;
  int _wrongMoves = 0;

  // Her günlük çözüm oturumunun kendine ait ID ve puanı vardır.
  int? _sessionId;
  int? _sessionScore;

  // İlk başarılı günlük çözümün liderlik tablosuna kaydedilen puanı.
  int? _acceptedScore;


  // Bonus bulmaca
  BonusPuzzle? _bonusPuzzle;

  bool _isBonusMode = false;
  bool _isBonusLoading = false;
  bool _isBonusSubmitting = false;
  bool _isBonusCompleted = false;
  bool _isBonusExpired = false;
  bool _isBonusStarted = false;
  bool _isBonusEligible = false;
  bool _showBonusCompletionAnimation = false;

  String? _bonusError;
  String? _bonusHintSquare;
  String _bonusFeedback = 'Bonus bulmaca hazırlanıyor...';

  Position? _bonusPosition;
  Position? _bonusStartingPosition;
  Move? _bonusLastMove;

  Timer? _bonusTimer;

  int _bonusRemainingSeconds = _bonusDurationSeconds;
  int _bonusWrongMoves = 0;
  int _bonusNextMoveIndex = 0;
  int? _bonusScore;

  int get _liveScore {
    final score = 1000 -
        (_elapsedSeconds * 2) -
        (_wrongMoves * 150) -
        (_usedHint ? 250 : 0);

    return score < 0 ? 0 : score;
  }

  Side get _playerSide =>
      _startingPosition?.turn ?? Side.white;

  Side get _bonusPlayerSide =>
      _bonusStartingPosition?.turn ?? Side.white;

  bool get _canUseHint {
    final position = _position;

    return _hasStarted &&
        !_isLoading &&
        !_isSolved &&
        !_isSubmitting &&
        !_usedHint &&
        _sessionId != null &&
        position != null &&
        position.turn == _playerSide;
  }

  bool get _canUseBonusHint {
    final bonus = _bonusPuzzle;
    final position = _bonusPosition;

    return _isAuthenticated &&
        _isBonusMode &&
        _isBonusStarted &&
        !_isBonusLoading &&
        !_isBonusSubmitting &&
        !_isBonusCompleted &&
        !_isBonusExpired &&
        _bonusRemainingSeconds > 0 &&
        bonus != null &&
        !bonus.usedHint &&
        position != null &&
        position.turn == _bonusPlayerSide;
  }

  @override
  void initState() {
    super.initState();
    _loadTodayPuzzle();
  }

  Future<void> refreshPuzzle() async {
    if (!mounted ||
        _isLoading ||
        _isSubmitting ||
        _hasStarted ||
        _isSolved ||
        _isBonusMode) {
      return;
    }

    await _loadTodayPuzzle();
  }

  // MainNavigationPage tarafından hesap değişiminde çağrılır.
// Eski hesap isteklerini geçersiz kılar ve yeni bulmacayı yükler.
  void resetPuzzleForAccountChange() {
    if (!mounted) return;

    // Önceki hesap için devam eden istekleri geçersiz kıl.
    _puzzleGeneration++;
    _clearWrongMoveHighlight();

    _stopTimer();
    _stopBonusTimer();

    _controller?.dispose();
    _controller = null;

    // Yeni hesabın günlük bulmacasını yükle.
    _loadTodayPuzzle();
  }

  @override
  void dispose() {
    // Widget kapandığında devam eden bütün asenkron işlemleri geçersiz kıl.
    _puzzleGeneration++;
    _clearWrongMoveHighlight();

    _timer?.cancel();
    _bonusTimer?.cancel();
    _controller?.dispose();

    super.dispose();
  }

  // ------------------------------------------------------------
  // ORTAK YARDIMCI METOTLAR
  // ------------------------------------------------------------

  void _clearWrongMoveHighlight() {
    _wrongMoveTimer?.cancel();
    _wrongMoveTimer = null;
    _wrongMoveUci = null;
  }

  void _showWrongMoveHighlight(Move move) {
    if (!mounted) return;
    _clearWrongMoveHighlight();
    final generation = _puzzleGeneration;
    setState(() => _wrongMoveUci = move.uci);
    _wrongMoveTimer = Timer(_wrongMoveDuration, () {
      if (!_isCurrentGeneration(generation)) return;
      setState(_clearWrongMoveHighlight);
    });
  }

  Widget _buildWrongMoveHighlight(double boardSize, Side orientation) {
    final uci = _wrongMoveUci;
    if (uci == null || uci.length < 4) return const SizedBox.shrink();
    final squareSize = boardSize / 8;
    final squares = {uci.substring(0, 2), uci.substring(2, 4)};
    return Positioned.fill(
      child: IgnorePointer(
        child: Stack(
          children: [
            for (final square in squares)
              if (RegExp(r'^[a-h][1-8]$').hasMatch(square))
                Positioned(
                  left: squareSize * (orientation == Side.white
                      ? square.codeUnitAt(0) - 97
                      : 104 - square.codeUnitAt(0)),
                  top: squareSize * (orientation == Side.white
                      ? 8 - int.parse(square[1])
                      : int.parse(square[1]) - 1),
                  width: squareSize,
                  height: squareSize,
                  child: ColoredBox(
                    color: _wrongMoveColor.withValues(alpha: 0.55),
                  ),
                ),
          ],
        ),
      ),
    );
  }

  Widget _buildWrongMoveWarning() {
    return Positioned.fill(
      child: IgnorePointer(
        child: Center(
          child: AnimatedOpacity(
            opacity: _wrongMoveUci != null ? 1 : 0,
            duration: const Duration(milliseconds: 150),
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
              decoration: BoxDecoration(
                color: const Color(0xFF8B2020).withValues(alpha: 0.96),
                borderRadius: BorderRadius.circular(12),
              ),
              child: const Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(Icons.cancel_outlined, color: Colors.white, size: 24),
                  SizedBox(width: 10),
                  Text(
                    'Yanlış Hamle!',
                    style: TextStyle(
                      color: Colors.white,
                      fontSize: 18,
                      fontWeight: FontWeight.bold,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  Position _buildPositionFromPlayedMoves(
      String fen,
      List<String> playedMoves,
      )
  {
    Position position = Chess.fromSetup(Setup.parseFen(fen));

    for (final uci in playedMoves) {
      final move = NormalMove.fromUci(uci);

      if (!position.isLegal(move)) {
        throw FormatException(
          'Sunucudan geçersiz hamle geldi: $uci',
        );
      }

      position = position.play(move);
      debugPrint('APPLIED MOVE: $uci');
      debugPrint('POSITION AFTER MOVE: ${position.fen}');
      debugPrint('TURN AFTER MOVE: ${position.turn}');
    }

    return position;
  }

  Position _positionAfterFirstMove(
      String fen,
      List<String> playedMoves,
      ) {
    final initialPosition = Chess.fromSetup(
      Setup.parseFen(fen),
    );

    if (playedMoves.isEmpty) return initialPosition;

    final firstMove = NormalMove.fromUci(playedMoves.first);

    if (!initialPosition.isLegal(firstMove)) {
      throw FormatException(
        'Başlangıç hamlesi geçersiz: ${playedMoves.first}',
      );
    }

    return initialPosition.play(firstMove);
  }

  Move? _lastPlayedMove(List<String> playedMoves) {
    if (playedMoves.isEmpty) return null;

    return NormalMove.fromUci(playedMoves.last);
  }

  String _formatTime(int seconds) {
    final minutes = seconds ~/ 60;
    final remainingSeconds = seconds % 60;

    return '${minutes.toString().padLeft(2, '0')}:'
        '${remainingSeconds.toString().padLeft(2, '0')}';
  }

  // Liderlik tablosunu güvenli şekilde yeniler.
  // Generation değişmişse eski hesaptan gelen işlem yenileme yapamaz.
  Future<void> _refreshLeaderboardSafely({
    int? generation,
  }) async
  {
    if (!mounted) return;

    if (generation != null &&
        !_isCurrentGeneration(generation)) {
      return;
    }

    try {
      await context
          .read<LeaderboardProvider>()
          .refreshLeaderboard();

      if (generation != null &&
          !_isCurrentGeneration(generation)) {
        return;
      }
    } catch (error, stackTrace) {
      _debugLog('Liderlik tablosu yenileme hatası: $error');
      _debugLog('$stackTrace');
    }
  }

  // ------------------------------------------------------------
  // GÜNLÜK BULMACA
  // ------------------------------------------------------------

  Future<void> _loadTodayPuzzle() async {
    if (!mounted) return;

    // Yeni yükleme başlatılır; önceki isteklerin cevapları geçersiz olur.
    final generation = ++_puzzleGeneration;
    _clearWrongMoveHighlight();

    _stopTimer();
    _stopBonusTimer();

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
      _isAuthenticated = false;
      _showCompletionAnimation = false;

      _isBonusMode = false;
      _isBonusLoading = false;
      _isBonusSubmitting = false;
      _isBonusCompleted = false;
      _isBonusExpired = false;
      _isBonusStarted = false;
      _isBonusEligible = false;
      _showBonusCompletionAnimation = false;

      _bonusPuzzle = null;
      _bonusError = null;
      _bonusHintSquare = null;
      _bonusScore = null;
      _bonusRemainingSeconds = _bonusDurationSeconds;
      _bonusPosition = null;
      _bonusStartingPosition = null;
      _bonusLastMove = null;
      _bonusNextMoveIndex = 0;
      _bonusWrongMoves = 0;
      _bonusFeedback = 'Bonus bulmaca hazırlanıyor...';

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
      _dailyElapsedAtSync = 0;
      _dailyElapsedClock
        ..stop()
        ..reset();
      _wrongMoves = 0;

      _sessionId = null;
      _sessionScore = null;
      _acceptedScore = null;

    });

    try {
      final authProvider = context.read<AuthProvider>();

      await authProvider.initialize();

      if (!_isCurrentGeneration(generation)) return;

      final isAuthenticated = authProvider.isLoggedIn;

      final clientId = await _clientIdStorage.getClientId();

      if (!_isCurrentGeneration(generation)) return;

      final response = await _puzzleApiService.getTodayPuzzle(
        clientId: clientId,
      );

      if (!_isCurrentGeneration(generation)) return;

      if (!response.success || response.data == null) {
        throw Exception(
          response.message ?? 'Bulmaca bilgisi alınamadı.',
        );
      }

      final puzzle = response.data!;

      if (!_isCurrentGeneration(generation)) return;

      _puzzle = puzzle;
      _position = null;
      _startingPosition = null;

      setState(() {
        _isLoading = false;
        _isAuthenticated = isAuthenticated;

        _hasSubmitted = puzzle.isSolved;
        _isReplay = puzzle.isSolved;
        _isSolved = false;

        _acceptedScore = isAuthenticated
            ? puzzle.previousScore
            : null;

        _feedback = puzzle.isSolved
            ? 'Bu bulmacayı daha önce tamamladın.'
            : 'Hazır olduğunda bulmacaya başlayabilirsin.';
      });

      if (isAuthenticated && puzzle.isSolved) {
        await _loadBonusStatus();
      }
    } catch (error, stackTrace) {
      _debugLog('Puzzle yükleme hatası: $error');
      _debugLog('$stackTrace');

      if (!_isCurrentGeneration(generation)) return;

      setState(() {
        _isLoading = false;
        _errorMessage = error.toString();
      });
    }
  }

  Future<void> _startPuzzle() async {
    if (_hasStarted ||
        _isLoading ||
        _isSubmitting ||
        _puzzle == null) {
      return;
    }

    final generation = _puzzleGeneration;

    _clearWrongMoveHighlight();
    final isReplay = _hasSubmitted;

    _stopTimer();

    setState(() {
      _isSubmitting = true;
      _isSolved = false;
      _isReplay = isReplay;
      _hasStarted = false;

      _sessionId = null;
      _sessionScore = null;

      _elapsedSeconds = 0;
      _dailyElapsedAtSync = 0;
      _dailyElapsedClock
        ..stop()
        ..reset();
      _wrongMoves = 0;
      _usedHint = false;
      _hintSquare = null;
      _lastMove = null;
      _nextMoveIndex = 0;

      _submissionError = null;
      _feedback = 'Bulmaca oturumu başlatılıyor...';
    });

    try {
      final clientId = await _clientIdStorage.getClientId();

      if (!_isCurrentGeneration(generation)) return;

      final response =
      await _puzzleApiService.startPuzzleSession(
        clientId: clientId,
      );

      if (!_isCurrentGeneration(generation)) return;

      _debugLog("START PUZZLE DATA: ${response.data}");

      if (!response.success || response.data == null) {
        throw Exception(
          response.message ?? 'Bulmaca oturumu başlatılamadı.',
        );
      }

      final data = response.data!;

      final sessionId = data.sessionId;
      final fen = data.fen;

      if (sessionId == null) {
        throw Exception(
          'Sunucudan oturum numarası alınamadı.',
        );
      }

      if (fen == null || fen.trim().isEmpty) {
        throw Exception(
          'Sunucu bulmaca oturumu için FEN bilgisi döndürmedi.',
        );
      }

      final playedMoves = data.playedMoves;

      final currentPosition = _buildPositionFromPlayedMoves(
        fen,
        playedMoves,
      );

      _debugLog('POSITION AFTER OPPONENT MOVE: ${currentPosition.fen}');

      final startingPosition = _positionAfterFirstMove(
        fen,
        playedMoves,
      );

      final startedAtUtc = data.startedAtUtc;

      final serverCompleted = data.isCompleted || data.isSolved;

      _sessionId = sessionId;
      _position = currentPosition;
      _startingPosition = startingPosition;
      _lastMove = _lastPlayedMove(playedMoves);

      _nextMoveIndex = data.currentMoveIndex == 0
          ? playedMoves.length
          : data.currentMoveIndex;

      final responseTimeUtc =
          response.timestamp?.toUtc() ?? DateTime.now().toUtc();
      _dailyElapsedAtSync = startedAtUtc == null
          ? 0
          : responseTimeUtc
          .difference(startedAtUtc)
          .inSeconds
          .clamp(0, 86400)
          .toInt();
      _elapsedSeconds = _dailyElapsedAtSync;

      _wrongMoves = data.wrongMoves;
      _usedHint = data.usedHint;
      _hintSquare = data.hintSquare;

      _controller?.dispose();

      _controller = ChessboardController(
        game: _buildGame(currentPosition),
      );

      if (!_isCurrentGeneration(generation)) return;

      if (serverCompleted) {
        final sessionScore = data.score;

        setState(() {
          _isSubmitting = false;
          _isSolved = true;
          _hasSubmitted = true;
          _hasStarted = false;
          _sessionScore = sessionScore;

          if (!isReplay &&
              _isAuthenticated &&
              sessionScore != null) {
            _acceptedScore = sessionScore;
          }

          _feedback = 'Bu oturum zaten tamamlanmış.';
        });

        return;
      }

      setState(() {
        _isSubmitting = false;
        _hasStarted = true;
        _feedback = 'Sıra sende! Doğru hamleyi bul.';
      });

      _dailyElapsedClock
        ..reset()
        ..start();
      _startTimer();
    } catch (error, stackTrace) {
      _debugLog('Günlük oturum başlatma hatası: $error');
      _debugLog('$stackTrace');

      if (!_isCurrentGeneration(generation)) return;

      setState(() {
        _isSubmitting = false;
        _hasStarted = false;
        _submissionError = error.toString();
        _feedback = 'Bulmaca başlatılamadı.';
      });
    }
  }

  void _startTimer() {
    _timer?.cancel();

    final generation = _puzzleGeneration;

    _timer = Timer.periodic(
      const Duration(seconds: 1),
          (_) {
        if (!_isCurrentGeneration(generation) ||
            _isSolved ||
            !_hasStarted) {
          return;
        }

        setState(() {
          _elapsedSeconds = (_dailyElapsedAtSync +
              _dailyElapsedClock.elapsed.inSeconds)
              .clamp(0, 86400)
              .toInt();
        });
      },
    );
  }

  void _stopTimer() {
    _timer?.cancel();
    _timer = null;
    _dailyElapsedClock.stop();
  }

  GameData _buildGame(Position position) {
    return GameData(
      fen: position.fen,
      lastMove: _lastMove,
      playerSide: _playerSide == Side.white
          ? PlayerSide.white
          : PlayerSide.black,
      sideToMove: position.turn,
      validMoves: makeLegalMoves(position),
      kingSquareInCheck: position.isCheck
          ? position.board.kingOf(position.turn)
          : null,
    );
  }

  Future<void> _onMove(
      Move move, {
        bool? viaDragAndDrop,
      }) async {
    if (_isBonusMode) {
      await _onBonusMove(move);
      return;
    }

    if (!_hasStarted ||
        _isSolved ||
        _isLoading ||
        _isSubmitting) {
      _restoreBoard();
      return;
    }

    final position = _position;

    if (position == null) return;

    if (!position.isLegal(move)) {
      _restoreBoard();
      return;
    }

    await _submitDailyMove(move);
  }

  Future<void> _submitDailyMove(Move move) async {
    final sessionId = _sessionId;

    if (_isSubmitting ||
        !_hasStarted ||
        _isSolved ||
        sessionId == null) {
      return;
    }

    final generation = _puzzleGeneration;

    setState(() {
      _clearWrongMoveHighlight();
      _isSubmitting = true;
    });

    try {
      final clientId = await _clientIdStorage.getClientId();

      if (!_isCurrentGeneration(generation)) return;

      final response = await _puzzleApiService.submitPuzzleMove(
        sessionId: sessionId,
        uci: move.uci,
        clientId: clientId,
      );

      if (!_isCurrentGeneration(generation)) return;

      if (!response.success) {
        final errorCode = response.errorCode ?? '';

        setState(() {
          _isSubmitting = false;

          if (errorCode == 'PUZZLE_ALREADY_COMPLETED') {
            _isSolved = true;
            _hasSubmitted = true;
            _feedback = 'Bu oturum zaten tamamlanmış.';
          } else {
            _feedback =
                response.message ?? 'Hamle gönderilemedi.';
          }
        });

        _restoreBoard();
        return;
      }

      final data = response.data;

      if (data == null) {
        throw Exception('Sunucudan hamle bilgisi alınamadı.');
      }

      final playedMoves = data.playedMoves;
      debugPrint('SERVER PLAYED MOVES: $playedMoves');
      debugPrint('OLD MOVE INDEX: $_nextMoveIndex');
      debugPrint('NEW MOVE INDEX: ${data.currentMoveIndex}');
      final serverMoveIndex = data.currentMoveIndex ?? playedMoves.length;
      final serverWrongMoves = data.wrongMoves ?? _wrongMoves;
      final serverUsedHint = data.usedHint;
      final serverCompleted = data.isCompleted || data.isSolved;

      final oldMoveCount = _nextMoveIndex;

      if (playedMoves.length <= oldMoveCount) {
        setState(() {
          _isSubmitting = false;
          _wrongMoves = serverWrongMoves;
          _usedHint = serverUsedHint;
          _feedback = 'Yanlış hamle. Tekrar dene!';
        });

        _restoreBoard();
        _showWrongMoveHighlight(move);
        return;
      }

      final playerMoves = playedMoves
          .take(oldMoveCount + 1)
          .toList();

      final playerPosition = _buildPositionFromPlayedMoves(
        _startingPosition!.fen,
        playerMoves.skip(1).toList(),
      );

      _position = playerPosition;
      _lastMove = _lastPlayedMove(playerMoves);

      _wrongMoves = serverWrongMoves;
      _usedHint = serverUsedHint;

      _controller?.updatePosition(
        _buildGame(playerPosition),
        animate: true,
        resetPremove: true,
      );

      if (playedMoves.length > oldMoveCount + 1) {
        await Future.delayed(_opponentReplyDelay);

        if (!_isCurrentGeneration(generation)) return;
      }
      debugPrint('STARTING POSITION FEN: ${_startingPosition!.fen}');
      debugPrint('REPLAYED MOVES: ${playedMoves.skip(1).toList()}');
      final newPosition = _buildPositionFromPlayedMoves(
        _startingPosition!.fen,
        playedMoves.skip(1).toList(),
      );
      debugPrint('FINAL BOARD FEN: ${newPosition.fen}');
      debugPrint('FINAL BOARD TURN: ${newPosition.turn}');

      _position = newPosition;
      _lastMove = _lastPlayedMove(playedMoves);

      _nextMoveIndex = serverMoveIndex > 0
          ? serverMoveIndex
          : playedMoves.length;

      _hintSquare = data.hintSquare;

      _controller?.updatePosition(
        _buildGame(newPosition),
        animate: true,
        resetPremove: true,
      );

      if (playedMoves.length > oldMoveCount + 1) {
        await Future.delayed(_opponentAnimationDelay);
        if (!_isCurrentGeneration(generation)) return;
      }

      if (serverCompleted) {
        await _finishPuzzle(
          data,
          generation: generation,
        );
        return;
      }

      if (!_isCurrentGeneration(generation)) return;

      setState(() {
        _isSubmitting = false;
        _feedback = 'Doğru hamle! Sıra sende.';
        _hintSquare = null;
      });
    } catch (error, stackTrace) {
      _debugLog('Günlük hamle gönderme hatası: $error');
      _debugLog('$stackTrace');

      if (!_isCurrentGeneration(generation)) return;

      setState(() {
        _isSubmitting = false;
        _feedback = 'Hamle gönderilemedi. Tekrar deneyebilirsin.';
      });

      _restoreBoard();
    }
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

  Future<void> _useHint() async {
    final sessionId = _sessionId;

    if (!_canUseHint || sessionId == null) return;

    final generation = _puzzleGeneration;

    setState(() {
      _clearWrongMoveHighlight();
      _isSubmitting = true;
    });

    try {
      final clientId = await _clientIdStorage.getClientId();

      if (!_isCurrentGeneration(generation)) return;

      final response = await _puzzleApiService.usePuzzleHint(
        sessionId: sessionId,
        clientId: clientId,
      );

      if (!_isCurrentGeneration(generation)) return;

      if (!response.success || response.data == null) {
        throw Exception(
          response.message ?? 'İpucu kullanılamadı.',
        );
      }

      final data = response.data!;
      final hintSquare = data.hintSquare;

      if (hintSquare == null ||
          hintSquare.isEmpty) {
        throw Exception('Sunucu ipucu karesini döndürmedi.');
      }

      setState(() {
        _isSubmitting = false;
        _usedHint = true;
        _hintSquare = hintSquare;
        _feedback =
        'İpucu: $_hintSquare karesindeki taşı hareket ettir.';
      });
    } catch (error, stackTrace) {
      _debugLog('Günlük ipucu hatası: $error');
      _debugLog('$stackTrace');

      if (!_isCurrentGeneration(generation)) return;

      setState(() {
        _isSubmitting = false;
        _feedback = error.toString();
      });
    }
  }

  Future<void> _finishPuzzle(
      PuzzleActionResult data, {
        required int generation,
      }) async
  {
    if (!_isCurrentGeneration(generation) || _isSolved) return;

    _stopTimer();

    setState(() {
      _showCompletionAnimation = true;
      _feedback = 'Bulmaca tamamlandı!';
    });

    await Future.delayed(
      const Duration(milliseconds: 1500),
    );

    if (!_isCurrentGeneration(generation)) return;

    setState(() {
      _showCompletionAnimation = false;
    });

    final score = data.score;

    setState(() {
      _isSolved = true;
      _hasSubmitted = true;
      _isSubmitting = false;

      _sessionScore = score;

      if (!_isReplay &&
          _isAuthenticated &&
          score != null) {
        _acceptedScore = score;
      }

      _feedback = _isReplay
          ? 'Tekrar çözümü başarıyla tamamladın!'
          : 'Tebrikler! Bulmacayı başarıyla çözdün.';

      _submissionError = null;
    });

    // Misafirler liderlik tablosunu yenilemez ve bonus yüklemez.
    // Tekrar çözüm de ilk günlük puanı değiştirmez.
    if (_isAuthenticated && !_isReplay) {
      await _refreshLeaderboardSafely(
        generation: generation,
      );

      if (!_isCurrentGeneration(generation)) return;

      await _loadBonusStatus();
    }
  }

  void _restartPuzzle() {
    final position = _position;

    if (position == null || _isSubmitting) return;

    setState(() {
      _feedback =
      'Oturum sunucuda devam ediyor. Hamlelerini göndermeye devam edebilirsin.';
    });

    _restoreBoard();
  }

  // ------------------------------------------------------------
  // BONUS BULMACA
  // ------------------------------------------------------------

  Future<void> _loadBonusStatus() async {
    if (!_isAuthenticated ||
        _isBonusLoading ||
        !mounted) {
      return;
    }

    final generation = _puzzleGeneration;

    setState(() {
      _isBonusLoading = true;
    });

    try {
      final response = await _puzzleApiService.getBonusPuzzle();

      if (!_isCurrentGeneration(generation)) return;

      if (!response.success || response.data == null) {
        setState(() {
          _isBonusLoading = false;
          _isBonusEligible = false;
          _bonusError = response.message;
        });
        return;
      }

      final bonus = response.data!;

      setState(() {
        _bonusPuzzle = bonus;
        _isBonusEligible = bonus.isEligible;
        _isBonusCompleted = bonus.isCompleted;
        _isBonusExpired = bonus.isExpired;
        _isBonusStarted = bonus.startedAtUtc != null;
        _bonusRemainingSeconds =
            bonus.remainingSeconds ?? _bonusDurationSeconds;
        _bonusScore = bonus.score;
        _bonusWrongMoves = bonus.wrongMoves;
        _bonusHintSquare = bonus.hintSquare;
        _isBonusLoading = false;
        _bonusError = null;
      });
    } catch (error, stackTrace) {
      _debugLog('Bonus durum kontrol hatası: $error');
      _debugLog('$stackTrace');

      if (!_isCurrentGeneration(generation)) return;

      setState(() {
        _isBonusLoading = false;
        _isBonusEligible = false;
        _bonusError = error.toString();
      });
    }
  }

  Future<void> _startBonusPuzzle() async {
    if (!_isAuthenticated ||
        !_isBonusEligible ||
        _isBonusLoading ||
        _isBonusCompleted ||
        _isBonusExpired) {
      return;
    }

    final generation = _puzzleGeneration;

    setState(() {
      _isBonusLoading = true;
      _bonusError = null;
      _clearWrongMoveHighlight();
      _bonusFeedback = 'Bonus bulmaca hazırlanıyor...';
    });

    try {
      final response = await _puzzleApiService.getBonusPuzzle();

      if (!_isCurrentGeneration(generation)) return;

      if (!response.success || response.data == null) {
        throw Exception(
          response.message ?? 'Bonus bulmaca bilgisi alınamadı.',
        );
      }

      final bonus = response.data!;

      if (bonus.isExpired || bonus.isCompleted) {
        setState(() {
          _bonusPuzzle = bonus;
          _isBonusExpired = bonus.isExpired;
          _isBonusCompleted = bonus.isCompleted;
          _bonusScore = bonus.score;
          _bonusRemainingSeconds = bonus.remainingSeconds ?? 0;
          _isBonusLoading = false;
        });
        return;
      }

      final isStarted = bonus.startedAtUtc != null;

      _stopBonusTimer();
      _controller?.dispose();
      _controller = null;

      // Bonus henüz başlamadıysa hazırlık ekranını aç.
      // Bu aşamada sunucunun FEN döndürmemesi gerekir.
      if (!isStarted) {
        _bonusPuzzle = bonus;
        _bonusPosition = null;
        _bonusStartingPosition = null;
        _bonusLastMove = null;
        _bonusNextMoveIndex = 0;
        _bonusWrongMoves = bonus.wrongMoves;
        _bonusRemainingSeconds =
            bonus.remainingSeconds ?? _bonusDurationSeconds;
        _bonusScore = bonus.score;
        _bonusHintSquare = bonus.hintSquare;

        setState(() {
          _isBonusMode = true;
          _isBonusStarted = false;
          _isBonusCompleted = false;
          _isBonusExpired = false;
          _isBonusLoading = false;
          _bonusFeedback =
          'Hazır olduğunda bonus bulmacaya başlayabilirsin.';
        });

        return;
      }

      // Daha önce başlatılmış bonus oturumuna devam ediliyor.
      // Bu durumda GET /bonus FEN dönebilir.
      final fen = bonus.fen;

      if (fen == null || fen.trim().isEmpty) {
        throw Exception(
          'Başlatılmış bonus bulmaca için FEN bilgisi bulunamadı.',
        );
      }

      final playedMoves = bonus.playedMoves;

      final currentPosition = _buildPositionFromPlayedMoves(
        fen,
        playedMoves,
      );

      final startingPosition = _positionAfterFirstMove(
        fen,
        playedMoves,
      );

      if (!_isCurrentGeneration(generation)) return;

      _bonusPuzzle = bonus;
      _bonusPosition = currentPosition;
      _bonusStartingPosition = startingPosition;
      _bonusLastMove = _lastPlayedMove(playedMoves);
      _bonusNextMoveIndex = playedMoves.length;
      _bonusWrongMoves = bonus.wrongMoves;
      _bonusRemainingSeconds =
          bonus.remainingSeconds ?? _bonusDurationSeconds;
      _bonusScore = bonus.score;
      _bonusHintSquare = bonus.hintSquare;

      _controller = ChessboardController(
        game: _buildBonusGame(currentPosition),
      );

      setState(() {
        _isBonusMode = true;
        _isBonusStarted = true;
        _isBonusCompleted = false;
        _isBonusExpired = false;
        _isBonusLoading = false;
        _bonusFeedback = 'Bonus denemen kaldığı yerden devam ediyor.';
      });

      _startBonusTimer();
    } catch (error, stackTrace) {
      _debugLog('Bonus hazırlama hatası: $error');
      _debugLog('$stackTrace');

      if (!_isCurrentGeneration(generation)) return;

      setState(() {
        _isBonusLoading = false;
        _bonusError = error.toString();
      });
    }
  }

  Future<void> _beginBonusPuzzle() async {
    if (!_isAuthenticated ||
        !_isBonusMode ||
        _isBonusStarted ||
        _isBonusLoading ||
        _isBonusCompleted ||
        _isBonusExpired) {
      return;
    }

    final generation = _puzzleGeneration;

    setState(() {
      _isBonusLoading = true;
      _bonusError = null;
      _bonusFeedback = 'Bonus bulmaca başlatılıyor...';
    });

    try {
      final response =
      await _puzzleApiService.startBonusPuzzle();

      if (!_isCurrentGeneration(generation)) return;

      if (!response.success || response.data == null) {
        throw Exception(
          response.message ?? 'Bonus bulmaca başlatılamadı.',
        );
      }

      final bonus = response.data!;

      if (bonus.isExpired || bonus.isCompleted) {
        setState(() {
          _bonusPuzzle = bonus;
          _isBonusExpired = bonus.isExpired;
          _isBonusCompleted = bonus.isCompleted;
          _bonusScore = bonus.score;
          _bonusRemainingSeconds = 0;
          _isBonusLoading = false;
        });

        _stopBonusTimer();
        return;
      }

      if (bonus.startedAtUtc == null) {
        throw Exception(
          'Sunucu bonus başlangıç zamanını döndürmedi.',
        );
      }

      final fen = bonus.fen;

      if (fen == null || fen.trim().isEmpty) {
        throw Exception(
          'Sunucu başlatılan bonus bulmaca için FEN bilgisi döndürmedi.',
        );
      }

      final playedMoves = bonus.playedMoves;

      final currentPosition = _buildPositionFromPlayedMoves(
        fen,
        playedMoves,
      );

      final startingPosition = _positionAfterFirstMove(
        fen,
        playedMoves,
      );

      if (!_isCurrentGeneration(generation)) return;

      _bonusPuzzle = bonus;
      _bonusPosition = currentPosition;
      _bonusStartingPosition = startingPosition;
      _bonusLastMove = _lastPlayedMove(playedMoves);
      _bonusNextMoveIndex = playedMoves.length;
      _bonusWrongMoves = bonus.wrongMoves;
      _bonusHintSquare = bonus.hintSquare;
      _bonusScore = bonus.score;

      _controller?.dispose();

      _controller = ChessboardController(
        game: _buildBonusGame(currentPosition),
      );

      setState(() {
        _isBonusStarted = true;
        _isBonusCompleted = false;
        _isBonusExpired = false;
        _bonusRemainingSeconds =
            bonus.remainingSeconds ?? _bonusDurationSeconds;
        _isBonusLoading = false;
        _bonusFeedback = '5 dakikalık süren başladı! Doğru hamleyi bul.';
      });

      _startBonusTimer();
    } catch (error, stackTrace) {
      _debugLog('Bonus başlatma hatası: $error');
      _debugLog('$stackTrace');

      if (!_isCurrentGeneration(generation)) return;

      setState(() {
        _isBonusLoading = false;
        _bonusError = error.toString();
        _bonusFeedback = 'Bonus başlatılamadı.';
      });
    }
  }

  GameData _buildBonusGame(Position position) {
    return GameData(
      fen: position.fen,
      lastMove: _bonusLastMove,
      playerSide: _bonusPlayerSide == Side.white
          ? PlayerSide.white
          : PlayerSide.black,
      sideToMove: position.turn,
      validMoves: makeLegalMoves(position),
      kingSquareInCheck: position.isCheck
          ? position.board.kingOf(position.turn)
          : null,
    );
  }

  void _startBonusTimer() {
    _bonusTimer?.cancel();

    final generation = _puzzleGeneration;

    _bonusTimer = Timer.periodic(
      const Duration(seconds: 1),
          (_) {
        if (!_isCurrentGeneration(generation) ||
            !_isBonusStarted ||
            _isBonusCompleted ||
            _isBonusExpired) {
          return;
        }

        if (_bonusRemainingSeconds <= 1) {
          _bonusTimer?.cancel();

          setState(() {
            _bonusRemainingSeconds = 0;
            _bonusFeedback =
            'Süre doldu! Bonus durumu kontrol ediliyor.';
          });

          _checkBonusExpiration();
          return;
        }

        setState(() {
          _bonusRemainingSeconds--;
        });
      },
    );
  }

  void _stopBonusTimer() {
    _bonusTimer?.cancel();
    _bonusTimer = null;
  }

  Future<void> _checkBonusExpiration() async {
    if (!mounted ||
        !_isAuthenticated ||
        !_isBonusMode) {
      return;
    }

    final generation = _puzzleGeneration;

    try {
      final response = await _puzzleApiService.getBonusPuzzle();

      if (!_isCurrentGeneration(generation)) return;

      if (!response.success || response.data == null) {
        setState(() {
          _bonusFeedback =
          'Süre doldu. Sunucu durumu henüz doğrulanamadı.';
        });
        return;
      }

      final bonus = response.data!;

      setState(() {
        _bonusPuzzle = bonus;
        _isBonusExpired = bonus.isExpired;
        _isBonusCompleted = bonus.isCompleted;
        _bonusRemainingSeconds = bonus.remainingSeconds ?? 0;
        _bonusScore = bonus.score;

        _bonusFeedback = bonus.isExpired
            ? 'Bonus süresi doldu.'
            : bonus.isCompleted
            ? 'Bonus bulmaca tamamlandı.'
            : 'Bonus durumu sunucudan kontrol edildi.';
      });

      if (bonus.isExpired || bonus.isCompleted) {
        _stopBonusTimer();
      }
    } catch (error) {
      _debugLog('Bonus süre kontrol hatası: $error');
    }
  }

  Future<void> _onBonusMove(Move move) async {
    if (!_isAuthenticated ||
        !_isBonusMode ||
        !_isBonusStarted ||
        _isBonusLoading ||
        _isBonusSubmitting ||
        _isBonusCompleted ||
        _isBonusExpired ||
        _bonusRemainingSeconds <= 0) {
      _restoreBonusBoard();
      return;
    }

    final generation = _puzzleGeneration;

    final position = _bonusPosition;
    final bonus = _bonusPuzzle;

    if (position == null || bonus == null) return;

    if (!position.isLegal(move)) {
      _restoreBonusBoard();
      return;
    }

    setState(() {
      _clearWrongMoveHighlight();
      _isBonusLoading = true;
    });

    try {
      final response = await _puzzleApiService.submitBonusMove(
        uci: move.uci,
      );

      if (!_isCurrentGeneration(generation)) return;

      if (!response.success) {
        final errorCode = response.errorCode ?? '';

        setState(() {
          _isBonusLoading = false;

          if (errorCode == 'BONUS_TIME_EXPIRED') {
            _bonusRemainingSeconds = 0;
            _isBonusExpired = true;
            _bonusFeedback = 'Bonus süresi doldu.';
          } else if (errorCode == 'BONUS_ALREADY_COMPLETED') {
            _isBonusCompleted = true;
            _bonusFeedback = 'Bonus bulmaca zaten tamamlandı.';
          } else {
            _bonusFeedback =
                response.message ?? 'Hamle gönderilemedi.';
          }
        });

        if (_isBonusExpired || _isBonusCompleted) {
          _stopBonusTimer();
        }

        _restoreBonusBoard();
        return;
      }

      final data = response.data;

      if (data == null) {
        throw Exception('Sunucudan hamle bilgisi alınamadı.');
      }

      final playedMoves = data.playedMoves;
      final serverWrongMoves = data.wrongMoves ?? _bonusWrongMoves;
      final serverMoveIndex = data.currentMoveIndex ?? playedMoves.length;
      final serverCompleted = data.isCompleted || data.isSolved;
      final serverExpired = data.isExpired;

      final oldMoveCount = _bonusNextMoveIndex;

      if (playedMoves.length <= oldMoveCount) {
        setState(() {
          _isBonusLoading = false;
          _bonusWrongMoves = serverWrongMoves;
          _bonusFeedback = 'Yanlış hamle. Tekrar dene!';
        });

        _restoreBonusBoard();
        if (_isBonusMode && !_isBonusExpired && !_isBonusCompleted &&
            !serverExpired && !serverCompleted && _bonusRemainingSeconds > 0) {
          _showWrongMoveHighlight(move);
        }
        return;
      }

      final playerMoves = playedMoves
          .take(oldMoveCount + 1)
          .toList();

      final playerPosition = _buildPositionFromPlayedMoves(
        bonus.fen!,
        playerMoves,
      );

      _bonusPosition = playerPosition;
      _bonusLastMove = _lastPlayedMove(playerMoves);

      _bonusWrongMoves = serverWrongMoves;

      _controller?.updatePosition(
        _buildBonusGame(playerPosition),
        animate: true,
        resetPremove: true,
      );

      if (playedMoves.length > oldMoveCount + 1) {
        await Future.delayed(_opponentReplyDelay);

        if (!_isCurrentGeneration(generation)) return;
      }

      final newPosition = _buildPositionFromPlayedMoves(
        bonus.fen!,
        playedMoves,
      );

      _bonusPosition = newPosition;
      _bonusLastMove = _lastPlayedMove(playedMoves);

      _bonusNextMoveIndex = serverMoveIndex > 0
          ? serverMoveIndex
          : playedMoves.length;

      _bonusHintSquare = null;

      _controller?.updatePosition(
        _buildBonusGame(newPosition),
        animate: true,
        resetPremove: true,
      );

      if (playedMoves.length > oldMoveCount + 1) {
        await Future.delayed(_opponentAnimationDelay);
        if (!_isCurrentGeneration(generation)) return;
      }

      setState(() {
        _isBonusLoading = false;
        _bonusFeedback = 'Doğru hamle!';
        _isBonusExpired = serverExpired;
        _isBonusCompleted = serverCompleted;
      });

      if (_isBonusExpired) {
        _stopBonusTimer();
        return;
      }

      if (_isBonusCompleted) {
        await _finishBonusPuzzle(
          generation: generation,
        );
      }
    } catch (error, stackTrace) {
      _debugLog('Bonus hamle hatası: $error');
      _debugLog('$stackTrace');

      if (!_isCurrentGeneration(generation)) return;

      setState(() {
        _isBonusLoading = false;
        _bonusFeedback =
        'Hamle gönderilemedi. Tekrar deneyebilirsin.';
      });

      _restoreBonusBoard();
    }
  }

  void _restoreBonusBoard() {
    final position = _bonusPosition;

    if (position == null) return;

    _controller?.updatePosition(
      _buildBonusGame(position),
      animate: false,
      resetPremove: true,
    );
  }

  Future<void> _useBonusHint() async {
    if (!_canUseBonusHint) return;

    final generation = _puzzleGeneration;

    setState(() {
      _isBonusLoading = true;
    });

    try {
      final response =
      await _puzzleApiService.useBonusHint();

      if (!_isCurrentGeneration(generation)) return;

      if (!response.success || response.data == null) {
        throw Exception(
          response.message ?? 'Bonus ipucu kullanılamadı.',
        );
      }

      final data = response.data!;
      final hintSquare = data.hintSquare;

      if (hintSquare == null ||
          hintSquare.isEmpty) {
        throw Exception('Sunucu ipucu karesini döndürmedi.');
      }

      setState(() {
        _isBonusLoading = false;
        _bonusHintSquare = hintSquare;
        _bonusFeedback =
        'İpucu: $_bonusHintSquare karesindeki taşı hareket ettir.';
      });
    } catch (error, stackTrace) {
      _debugLog('Bonus ipucu hatası: $error');
      _debugLog('$stackTrace');

      if (!_isCurrentGeneration(generation)) return;

      setState(() {
        _isBonusLoading = false;
        _bonusFeedback = error.toString();
      });
    }
  }

  Future<void> _finishBonusPuzzle({
    required int generation,
  }) async {
    if (!_isCurrentGeneration(generation) ||
        !_isAuthenticated ||
        _isBonusSubmitting) {
      return;
    }

    _stopBonusTimer();

    setState(() {
      _isBonusSubmitting = true;
      _bonusFeedback = 'Bonus sonucu kaydediliyor...';
    });

    try {
      final response =
      await _puzzleApiService.submitBonusPuzzle();

      if (!_isCurrentGeneration(generation)) return;

      if (!response.success) {
        throw Exception(
          response.message ?? 'Bonus sonucu kaydedilemedi.',
        );
      }

      final score = response.data?.score;

      setState(() {
        _bonusScore = score;
        _isBonusCompleted = true;
        _isBonusExpired = false;
        _isBonusSubmitting = false;
        _isBonusLoading = false;
        _bonusRemainingSeconds = 0;
        _bonusFeedback = score != null
            ? 'Tebrikler! Bonus puanın kaydedildi.'
            : 'Bonus tamamlandı.';

        _showBonusCompletionAnimation = true;
      });

      await _refreshLeaderboardSafely(
        generation: generation,
      );

      if (!_isCurrentGeneration(generation)) return;

      await Future.delayed(
        const Duration(milliseconds: 1800),
      );

      if (!_isCurrentGeneration(generation)) return;

      setState(() {
        _showBonusCompletionAnimation = false;
      });
    } catch (error, stackTrace) {
      _debugLog('Bonus sonuç kaydetme hatası: $error');
      _debugLog('$stackTrace');

      if (!_isCurrentGeneration(generation)) return;

      setState(() {
        _isBonusSubmitting = false;
        _bonusFeedback =
        'Bonus çözüldü ancak sonuç kaydedilemedi: $error';
      });
    }
  }

  void _returnToDailyPuzzle() {
    _clearWrongMoveHighlight();
    _stopBonusTimer();

    final position = _position;

    if (position == null) return;

    _controller?.dispose();

    _controller = ChessboardController(
      game: _buildGame(position),
    );

    setState(() {
      _isBonusMode = false;
      _bonusFeedback = 'Bonus ekranından ayrıldın.';
    });

    if (_isAuthenticated) {
      _loadBonusStatus();
    }
  }

  // ------------------------------------------------------------
  // SAYFA
  // ------------------------------------------------------------

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(
          _isBonusMode ? 'Bonus Bulmaca' : 'Günün Bulmacası',
          style: const TextStyle(fontWeight: FontWeight.bold),
        ),
        leading: _isBonusMode
            ? IconButton(
          onPressed: _returnToDailyPuzzle,
          icon: const Icon(Icons.arrow_back),
          tooltip: 'Günlük bulmacaya dön',
        )
            : null,
        actions: [
          if (!_isBonusMode)
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

    if (_isBonusMode) {
      if (_isBonusCompleted && _showBonusCompletionAnimation) {
        return _buildBonusPuzzleBody();
      }

      if (_isBonusCompleted || _isBonusExpired) {
        return _buildBonusResultBody();
      }

      return _buildBonusPuzzleBody();
    }

    if (_puzzle == null) {
      return const Center(
        child: Text('Bulmaca bulunamadı.'),
      );
    }

    if (!_hasStarted &&
        (_position == null || _controller == null)) {
      return _buildStartOverlay();
    }

    if (_position == null || _controller == null) {
      return const Center(
        child: Text('Bulmaca tahtası oluşturulamadı.'),
      );
    }

    if (_isSolved) {
      return _buildResultBody();
    }

    return _buildPuzzleBody();
  }

  // ------------------------------------------------------------
  // GÜNLÜK BULMACA ARAYÜZÜ
  // ------------------------------------------------------------

  Widget _buildPuzzleBody() {
    return LayoutBuilder(
      builder: (context, constraints) {
        final boardSize = constraints.maxWidth - 32;

        return SizedBox(
          width: constraints.maxWidth,
          height: constraints.maxHeight,
          child: Stack(
            children: [
              ImageFiltered(
                imageFilter: ImageFilter.blur(
                  sigmaX: _hasStarted ? 0 : 7,
                  sigmaY: _hasStarted ? 0 : 7,
                ),
                child: IgnorePointer(
                  ignoring: !_hasStarted,
                  child: Column(
                    children: [
                      Padding(
                        padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
                        child: _buildPuzzleInfo(),
                      ),
                      const SizedBox(height: 12),
                      Center(
                        child: SizedBox(
                          width: boardSize,
                          height: boardSize,
                          child: Stack(
                            alignment: Alignment.center,
                            children: [
                              AbsorbPointer(
                                absorbing: _isSubmitting,
                                child: Chessboard(
                                  key: ValueKey('daily-puzzle-$_hasStarted'),
                                  controller: _controller!,
                                  size: boardSize,
                                  orientation: _playerSide,
                                  onMove: _onMove,
                                ),
                              ),
                              _buildWrongMoveHighlight(boardSize, _playerSide),
                              _buildWrongMoveWarning(),
                              IgnorePointer(
                                child: AnimatedOpacity(
                                  opacity: _showCompletionAnimation ? 1 : 0,
                                  duration: const Duration(milliseconds: 300),
                                  child: AnimatedScale(
                                    scale: _showCompletionAnimation ? 1 : 0.8,
                                    duration: const Duration(milliseconds: 400),
                                    curve: Curves.easeOutBack,
                                    child: Container(
                                      width: boardSize,
                                      height: boardSize,
                                      decoration: BoxDecoration(
                                        color: Colors.black.withValues(alpha: 0.5),
                                        borderRadius: BorderRadius.circular(8),
                                      ),
                                      child: Column(
                                        mainAxisAlignment: MainAxisAlignment.center,
                                        children: [
                                          const Icon(
                                            Icons.check_circle_rounded,
                                            color: Color(0xFF69E09B),
                                            size: 76,
                                          ),
                                          const SizedBox(height: 16),
                                          const Text(
                                            'Bulmaca Tamamlandı!',
                                            textAlign: TextAlign.center,
                                            style: TextStyle(
                                              color: Colors.white,
                                              fontSize: 23,
                                              fontWeight: FontWeight.bold,
                                            ),
                                          ),
                                          const SizedBox(height: 8),
                                          Text(
                                            'Harika hamle!',
                                            style: TextStyle(
                                              color: Colors.green.shade200,
                                              fontSize: 16,
                                            ),
                                          ),
                                        ],
                                      ),
                                    ),
                                  ),
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                      const SizedBox(height: 12),
                      Expanded(
                        child: SingleChildScrollView(
                          padding: const EdgeInsets.fromLTRB(16, 4, 16, 28),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              _buildTimerAndMistakes(),
                              const SizedBox(height: 12),
                              _buildLiveScoreCard(),
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
                    ],
                  ),
                ),
              ),
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

  // ------------------------------------------------------------
  // BONUS BULMACA ARAYÜZÜ
  // ------------------------------------------------------------

  Widget _buildBonusPuzzleBody() {
    final position = _bonusPosition;
    final controller = _controller;

    if (!_isBonusStarted &&
        position == null &&
        controller == null) {
      return _buildBonusStartOverlay();
    }

    if (position == null || controller == null) {
      return Center(
        child: _isBonusLoading
            ? const CircularProgressIndicator(color: _primary)
            : const Text('Bonus bulmaca yüklenemedi.'),
      );
    }

    return LayoutBuilder(
      builder: (context, constraints) {
        final boardSize = constraints.maxWidth - 32;

        return SizedBox(
          width: constraints.maxWidth,
          height: constraints.maxHeight,
          child: Stack(
            children: [
              Column(
                children: [
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
                    child: Container(
                      padding: const EdgeInsets.all(16),
                      decoration: BoxDecoration(
                        color: _surface,
                        borderRadius: BorderRadius.circular(16),
                      ),
                      child: Row(
                        children: [
                          const Icon(
                            Icons.bolt,
                            color: _secondary,
                            size: 30,
                          ),
                          const SizedBox(width: 12),
                          const Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  'Bonus Bulmaca',
                                  style: TextStyle(
                                    fontWeight: FontWeight.bold,
                                    fontSize: 17,
                                  ),
                                ),
                                SizedBox(height: 4),
                                Text(
                                  'Ek puan kazanmak için 300 saniyen var.',
                                  style: TextStyle(
                                    color: Colors.white60,
                                    fontSize: 12,
                                  ),
                                ),
                              ],
                            ),
                          ),
                          Text(
                            '$_bonusRemainingSeconds sn',
                            style: TextStyle(
                              fontSize: 22,
                              fontWeight: FontWeight.bold,
                              color: _bonusRemainingSeconds <= 10
                                  ? const Color(0xFFE69A91)
                                  : _secondary,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                  const SizedBox(height: 12),
                  Center(
                    child: SizedBox(
                      width: boardSize,
                      height: boardSize,
                      child: Stack(
                        children: [
                          AbsorbPointer(
                            absorbing: _isBonusLoading,
                            child: Chessboard(
                              key: ValueKey('bonus-puzzle-$_isBonusStarted'),
                              controller: controller,
                              size: boardSize,
                              orientation: _bonusPlayerSide,
                              onMove: _onMove,
                            ),
                          ),
                          _buildWrongMoveHighlight(boardSize, _bonusPlayerSide),
                          _buildWrongMoveWarning(),
                          IgnorePointer(
                            child: AnimatedOpacity(
                              opacity: _showBonusCompletionAnimation ? 1 : 0,
                              duration: const Duration(milliseconds: 300),
                              child: AnimatedScale(
                                scale: _showBonusCompletionAnimation ? 1 : 0.8,
                                duration: const Duration(milliseconds: 400),
                                curve: Curves.easeOutBack,
                                child: Container(
                                  decoration: BoxDecoration(
                                    color: Colors.black.withValues(alpha: 0.5),
                                    borderRadius: BorderRadius.circular(8),
                                  ),
                                  child: const Center(
                                    child: Column(
                                      mainAxisSize: MainAxisSize.min,
                                      children: [
                                        Icon(
                                          Icons.check_circle_rounded,
                                          color: Color(0xFF7ED6A5),
                                          size: 76,
                                        ),
                                        SizedBox(height: 14),
                                        Text(
                                          'Bulmaca Tamamlandı!',
                                          style: TextStyle(
                                            fontSize: 21,
                                            fontWeight: FontWeight.bold,
                                            color: Colors.white,
                                          ),
                                        ),
                                        SizedBox(height: 6),
                                        Text(
                                          'Harika hamle!',
                                          style: TextStyle(
                                            fontSize: 14,
                                            color: Colors.white70,
                                          ),
                                        ),
                                      ],
                                    ),
                                  ),
                                ),
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                  const SizedBox(height: 12),
                  Expanded(
                    child: IgnorePointer(
                      ignoring: !_isBonusStarted,
                      child: ImageFiltered(
                        imageFilter: ImageFilter.blur(
                          sigmaX: _isBonusStarted ? 0 : 7,
                          sigmaY: _isBonusStarted ? 0 : 7,
                        ),
                        child: SingleChildScrollView(
                          padding: const EdgeInsets.fromLTRB(16, 4, 16, 24),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              Row(
                                children: [
                                  Expanded(
                                    child: _buildMiniStatistic(
                                      icon: Icons.timer_outlined,
                                      label: 'Kalan süre',
                                      value: '$_bonusRemainingSeconds sn',
                                      color: _secondary,
                                    ),
                                  ),
                                  const SizedBox(width: 12),
                                  Expanded(
                                    child: _buildMiniStatistic(
                                      icon: Icons.close_rounded,
                                      label: 'Yanlış hamle',
                                      value: '$_bonusWrongMoves',
                                      color: const Color(0xFFE69A91),
                                    ),
                                  ),
                                ],
                              ),
                              const SizedBox(height: 14),
                              _buildBonusFeedback(),
                              if (_bonusHintSquare != null) ...[
                                const SizedBox(height: 12),
                                _buildBonusHintCard(),
                              ],
                              const SizedBox(height: 14),
                              Row(
                                children: [
                                  Expanded(
                                    child: OutlinedButton.icon(
                                      onPressed: _returnToDailyPuzzle,
                                      icon: const Icon(Icons.arrow_back),
                                      label: const Text('Geri Dön'),
                                      style: OutlinedButton.styleFrom(
                                        foregroundColor: Colors.white70,
                                        padding: const EdgeInsets.symmetric(
                                          vertical: 14,
                                        ),
                                      ),
                                    ),
                                  ),
                                  const SizedBox(width: 12),
                                  Expanded(
                                    child: ElevatedButton.icon(
                                      onPressed: _canUseBonusHint
                                          ? _useBonusHint
                                          : null,
                                      icon: const Icon(Icons.lightbulb_outline),
                                      label: const Text('İpucu Al'),
                                      style: ElevatedButton.styleFrom(
                                        backgroundColor: _secondary,
                                        foregroundColor: _background,
                                        padding: const EdgeInsets.symmetric(
                                          vertical: 14,
                                        ),
                                      ),
                                    ),
                                  ),
                                ],
                              ),
                              if (_isBonusLoading) ...[
                                const SizedBox(height: 12),
                                const Center(
                                  child: CircularProgressIndicator(
                                    color: _primary,
                                  ),
                                ),
                              ],
                            ],
                          ),
                        ),
                      ),
                    ),
                  ),
                ],
              ),
              if (!_isBonusStarted)
                Positioned.fill(
                  child: _buildBonusStartOverlay(),
                ),
            ],
          ),
        );
      },
    );
  }

  Widget _buildBonusStartOverlay() {
    return Container(
      color: _background.withValues(alpha: 0.65),
      alignment: Alignment.center,
      padding: const EdgeInsets.all(24),
      child: Container(
        width: double.infinity,
        padding: const EdgeInsets.all(24),
        decoration: BoxDecoration(
          color: _surface,
          borderRadius: BorderRadius.circular(22),
          border: Border.all(
            color: _secondary.withValues(alpha: 0.35),
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
                color: _secondary.withValues(alpha: 0.13),
                shape: BoxShape.circle,
              ),
              child: const Icon(
                Icons.bolt,
                color: _secondary,
                size: 36,
              ),
            ),
            const SizedBox(height: 18),
            const Text(
              'Bonus Bulmaca Seni Bekliyor!',
              textAlign: TextAlign.center,
              style: TextStyle(
                fontSize: 21,
                fontWeight: FontWeight.bold,
              ),
            ),
            const SizedBox(height: 12),
            const Text(
              'Bonus bulmacayı çözerek ek puan kazanabilirsin. '
                  '5 dakikalık süren yalnızca başla butonuna '
                  'bastığında başlayacak.',
              textAlign: TextAlign.center,
              style: TextStyle(
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
                onPressed: _isBonusLoading ? null : _beginBonusPuzzle,
                icon: _isBonusLoading
                    ? const SizedBox(
                  width: 19,
                  height: 19,
                  child: CircularProgressIndicator(
                    strokeWidth: 2,
                    color: _background,
                  ),
                )
                    : const Icon(Icons.play_arrow_rounded, size: 27),
                label: Text(
                  _isBonusLoading
                      ? 'Başlatılıyor...'
                      : 'Bonus Bulmacaya Başla',
                  style: const TextStyle(
                    fontSize: 15,
                    fontWeight: FontWeight.bold,
                  ),
                ),
                style: ElevatedButton.styleFrom(
                  backgroundColor: _secondary,
                  foregroundColor: _background,
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(14),
                  ),
                ),
              ),
            ),
            const SizedBox(height: 10),
            SizedBox(
              width: double.infinity,
              height: 46,
              child: TextButton.icon(
                onPressed: _isBonusLoading ? null : _returnToDailyPuzzle,
                icon: const Icon(Icons.arrow_back),
                label: const Text('Günlük Bulmacaya Dön'),
                style: TextButton.styleFrom(
                  foregroundColor: Colors.white70,
                ),
              ),
            ),
            if (_bonusError != null) ...[
              const SizedBox(height: 12),
              Text(
                _bonusError!,
                textAlign: TextAlign.center,
                style: const TextStyle(
                  color: Color(0xFFE69A91),
                  fontSize: 12,
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }

  Widget _buildBonusFeedback() {
    final isWrong = _bonusFeedback.toLowerCase().contains('yanlış');
    final color = isWrong ? const Color(0xFFE69A91) : _primary;

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
            isWrong ? Icons.close_rounded : Icons.info_outline,
            color: color,
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              _bonusFeedback,
              style: const TextStyle(fontSize: 13),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildBonusHintCard() {
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: _secondary.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(14),
      ),
      child: Row(
        children: [
          const Icon(Icons.lightbulb, color: _secondary),
          const SizedBox(width: 10),
          const Expanded(
            child: Text('Doğru hamlenin başlangıç karesi:'),
          ),
          Text(
            _bonusHintSquare!,
            style: const TextStyle(
              color: _secondary,
              fontWeight: FontWeight.bold,
              fontSize: 18,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildBonusResultBody() {
    final expired = _isBonusExpired;

    return SingleChildScrollView(
      padding: const EdgeInsets.all(22),
      child: Column(
        children: [
          const SizedBox(height: 24),
          Icon(
            expired ? Icons.timer_off : Icons.emoji_events,
            size: 76,
            color: expired ? const Color(0xFFE69A91) : _secondary,
          ),
          const SizedBox(height: 20),
          Text(
            expired ? 'Bonus Süresi Doldu' : 'Bonus Tamamlandı!',
            style: const TextStyle(
              fontSize: 25,
              fontWeight: FontWeight.bold,
            ),
          ),
          const SizedBox(height: 12),
          Text(
            expired
                ? '5 dakikalık bonus süresini tamamlayamadın.'
                : 'Bonus bulmacayı başarıyla çözdün.',
            textAlign: TextAlign.center,
            style: const TextStyle(
              color: Colors.white70,
              fontSize: 14,
            ),
          ),
          const SizedBox(height: 28),
          Container(
            width: double.infinity,
            padding: const EdgeInsets.all(24),
            decoration: BoxDecoration(
              color: _surface,
              borderRadius: BorderRadius.circular(18),
              border: Border.all(
                color: _secondary.withValues(alpha: 0.25),
              ),
            ),
            child: Column(
              children: [
                const Text(
                  'Bonus puanın',
                  style: TextStyle(
                    color: Colors.white70,
                    fontSize: 15,
                  ),
                ),
                const SizedBox(height: 12),
                Text(
                  '${_bonusScore ?? 0}',
                  style: const TextStyle(
                    color: _secondary,
                    fontSize: 42,
                    fontWeight: FontWeight.bold,
                  ),
                ),
                const SizedBox(height: 8),
                Text(
                  expired
                      ? 'Bu denemeden puan kazanamadın.'
                      : 'Puanın sunucuya kaydedildi.',
                  style: const TextStyle(
                    color: Colors.white54,
                    fontSize: 12,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 24),
          SizedBox(
            width: double.infinity,
            height: 52,
            child: ElevatedButton.icon(
              onPressed: _returnToDailyPuzzle,
              icon: const Icon(Icons.arrow_back),
              label: const Text('Günlük Bulmacaya Dön'),
            ),
          ),
          if (_bonusFeedback.isNotEmpty) ...[
            const SizedBox(height: 16),
            Text(
              _bonusFeedback,
              textAlign: TextAlign.center,
              style: const TextStyle(
                color: Colors.white54,
                fontSize: 12,
              ),
            ),
          ],
        ],
      ),
    );
  }

  // ------------------------------------------------------------
  // GÜNLÜK BULMACA BAŞLANGIÇ EKRANI
  // ------------------------------------------------------------

  Widget _buildStartOverlay() {
    final canShowBonus = _isAuthenticated && _isBonusEligible;
    final bonusClosed = _isBonusCompleted || _isBonusExpired;

    String bonusButtonText;

    if (_isBonusCompleted) {
      bonusButtonText = 'Bonus Bulmaca Tamamlandı';
    } else if (_isBonusExpired) {
      bonusButtonText = 'Bonus Süresi Doldu';
    } else if (_isBonusStarted) {
      bonusButtonText = 'Bonus Bulmacaya Devam Et';
    } else {
      bonusButtonText = 'Bonus Bulmacayı Çöz';
    }

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
                  ? 'Günlük bulmacayı tamamladın. İstersen tekrar çözebilir veya bonus hakkını kullanabilirsin.'
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
                onPressed: _isSubmitting ? null : _startPuzzle,
                icon: const Icon(
                  Icons.replay_rounded,
                  size: 24,
                ),
                label: Text(
                  _hasSubmitted ? 'Tekrar Çöz' : 'Bulmacaya Başla',
                  style: const TextStyle(
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
            if (canShowBonus) ...[
              const SizedBox(height: 12),
              SizedBox(
                width: double.infinity,
                height: 54,
                child: OutlinedButton.icon(
                  onPressed: _isBonusLoading || bonusClosed
                      ? null
                      : _startBonusPuzzle,
                  icon: Icon(
                    _isBonusCompleted
                        ? Icons.emoji_events
                        : _isBonusExpired
                        ? Icons.timer_off
                        : Icons.bolt,
                  ),
                  label: Text(
                    _isBonusLoading
                        ? 'Yükleniyor...'
                        : bonusButtonText,
                    style: const TextStyle(
                      fontSize: 15,
                      fontWeight: FontWeight.bold,
                    ),
                  ),
                  style: OutlinedButton.styleFrom(
                    foregroundColor: _secondary,
                    side: BorderSide(
                      color: _secondary.withValues(alpha: 0.7),
                    ),
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(14),
                    ),
                  ),
                ),
              ),
            ],
            if (_isSubmitting || _isBonusLoading) ...[
              const SizedBox(height: 14),
              const CircularProgressIndicator(color: _primary),
            ],
            if (_submissionError != null) ...[
              const SizedBox(height: 12),
              Text(
                _submissionError!,
                textAlign: TextAlign.center,
                style: const TextStyle(
                  color: Color(0xFFE69A91),
                  fontSize: 12,
                ),
              ),
            ],
            if (_bonusError != null) ...[
              const SizedBox(height: 12),
              Text(
                _bonusError!,
                textAlign: TextAlign.center,
                style: const TextStyle(
                  color: Color(0xFFE69A91),
                  fontSize: 12,
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }

  // ------------------------------------------------------------
  // GÜNLÜK BULMACA BİLGİ KARTLARI
  // ------------------------------------------------------------

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

  Widget _buildLiveScoreCard() {
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: 18,
        vertical: 16,
      ),
      decoration: BoxDecoration(
        color: _primary.withValues(alpha: 0.09),
        borderRadius: BorderRadius.circular(16),
        border: Border.all(
          color: _primary.withValues(alpha: 0.25),
        ),
      ),
      child: Row(
        children: [
          Container(
            width: 44,
            height: 44,
            decoration: BoxDecoration(
              color: _primary.withValues(alpha: 0.13),
              borderRadius: BorderRadius.circular(13),
            ),
            child: const Icon(
              Icons.stars_rounded,
              color: _primary,
              size: 25,
            ),
          ),
          const SizedBox(width: 13),
          const Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Canlı puanın',
                  style: TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.bold,
                  ),
                ),
                SizedBox(height: 4),
                Text(
                  'Süre, yanlış hamle ve ipuçlarına göre güncellenir.',
                  style: TextStyle(
                    fontSize: 11,
                    color: Colors.white60,
                  ),
                ),
              ],
            ),
          ),
          Text(
            '$_liveScore',
            style: const TextStyle(
              fontSize: 27,
              fontWeight: FontWeight.bold,
              color: _primary,
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
            label: const Text('Tahtayı Yenile'),
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
            label: Text(
              _usedHint ? 'İpucu Kullanıldı' : 'İpucu Al',
            ),
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

  // ------------------------------------------------------------
  // SONUÇ EKRANI
  // ------------------------------------------------------------

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
              child: Icon(
                _isReplay
                    ? Icons.replay_rounded
                    : Icons.emoji_events,
                size: 64,
                color: _secondary,
              ),
            ),
          ),
          const SizedBox(height: 24),
          Text(
            _isReplay
                ? 'Tekrar Tebrikler!'
                : 'Harika İş Çıkardın!',
            textAlign: TextAlign.center,
            style: const TextStyle(
              fontSize: 26,
              fontWeight: FontWeight.bold,
            ),
          ),
          const SizedBox(height: 10),
          Text(
            _isReplay
                ? 'Bu oturumun puanı ayrıca hesaplandı. '
                'İlk günlük puanın ve liderlik tablosundaki kaydın korunuyor.'
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
          if (_isAuthenticated && _isBonusEligible) ...[
            const SizedBox(height: 22),
            _buildBonusEntryCard(),
          ],
          if (_isAuthenticated &&
              _isBonusLoading &&
              !_isBonusEligible) ...[
            const SizedBox(height: 18),
            const Center(
              child: CircularProgressIndicator(
                color: _primary,
              ),
            ),
          ],
          if (_submissionError != null) ...[
            const SizedBox(height: 16),
            _buildSubmissionError(),
          ],
          const SizedBox(height: 24),
          SizedBox(
            height: 52,
            child: ElevatedButton.icon(
              onPressed: _isLoading ? null : _loadTodayPuzzle,
              icon: const Icon(Icons.arrow_back),
              label: const Text(
                'Günlük Bulmacaya Dön',
                style: TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.bold,
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildBonusEntryCard() {
    final alreadyClosed = _isBonusCompleted || _isBonusExpired;
    final alreadyStarted = _isBonusStarted && !alreadyClosed;

    String description;

    if (_isBonusExpired) {
      description = 'Bugünkü bonus süren doldu.';
    } else if (_isBonusCompleted) {
      description = 'Bugünkü bonus bulmacanı tamamladın.';
    } else if (alreadyStarted) {
      description =
      'Bonus denemen başlatıldı. Süre işlemeye devam ediyor. '
          'Başlatılmış bir deneme yeniden başlatılamaz.';
    } else {
      description =
      'Hatasız çözümün sayesinde 5 dakikalık bonus bulmaca '
          'hakkı kazandın. İstediğin zaman başlatabilirsin.';
    }

    return Container(
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: _secondary.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(18),
        border: Border.all(
          color: _secondary.withValues(alpha: 0.35),
        ),
      ),
      child: Column(
        children: [
          Icon(
            alreadyClosed
                ? Icons.emoji_events
                : alreadyStarted
                ? Icons.timer
                : Icons.bolt,
            color: _secondary,
            size: 36,
          ),
          const SizedBox(height: 10),
          Text(
            alreadyClosed
                ? 'Bonus Bulmaca Sonucu'
                : alreadyStarted
                ? 'Bonus Denemen Başlatıldı'
                : 'Bonus Bulmaca Kazandın!',
            textAlign: TextAlign.center,
            style: const TextStyle(
              fontSize: 18,
              fontWeight: FontWeight.bold,
              color: _secondary,
            ),
          ),
          const SizedBox(height: 8),
          Text(
            description,
            textAlign: TextAlign.center,
            style: const TextStyle(
              color: Colors.white70,
              fontSize: 13,
              height: 1.4,
            ),
          ),
          if (alreadyStarted) ...[
            const SizedBox(height: 14),
            Text(
              'Sunucudaki kalan süre: $_bonusRemainingSeconds saniye',
              style: const TextStyle(
                color: _secondary,
                fontWeight: FontWeight.bold,
                fontSize: 14,
              ),
            ),
          ],
          if (!alreadyClosed && !alreadyStarted) ...[
            const SizedBox(height: 16),
            SizedBox(
              width: double.infinity,
              height: 50,
              child: ElevatedButton.icon(
                onPressed: _isBonusLoading ? null : _startBonusPuzzle,
                icon: const Icon(Icons.play_arrow),
                label: Text(
                  _isBonusLoading
                      ? 'Yükleniyor...'
                      : 'Bonus Bulmacayı Aç',
                ),
                style: ElevatedButton.styleFrom(
                  backgroundColor: _secondary,
                  foregroundColor: _background,
                ),
              ),
            ),
          ],
          if (alreadyClosed && _bonusScore != null) ...[
            const SizedBox(height: 12),
            Text(
              'Bonus puanı: $_bonusScore',
              style: const TextStyle(
                color: _primary,
                fontWeight: FontWeight.bold,
                fontSize: 16,
              ),
            ),
          ],
          if (_bonusError != null) ...[
            const SizedBox(height: 10),
            Text(
              _bonusError!,
              textAlign: TextAlign.center,
              style: const TextStyle(
                color: Color(0xFFE69A91),
                fontSize: 12,
              ),
            ),
          ],
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
          const Text(
            'Bu oturumun puanı',
            style: TextStyle(
              fontSize: 15,
              color: Colors.white70,
            ),
          ),
          const SizedBox(height: 8),
          Text(
            '${_sessionScore ?? 0}',
            style: const TextStyle(
              fontSize: 42,
              fontWeight: FontWeight.bold,
              color: _secondary,
            ),
          ),
          const SizedBox(height: 8),
          const Text(
            'Bu denemenin süre, hata ve ipucu bilgilerine göre hesaplanan puanı.',
            textAlign: TextAlign.center,
            style: TextStyle(
              fontSize: 12,
              color: Colors.white60,
            ),
          ),
          if (_isAuthenticated) ...[
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 20),
              child: Divider(color: Colors.white12),
            ),
            const Text(
              'Liderlik tablosuna kaydedilen günlük puanın',
              textAlign: TextAlign.center,
              style: TextStyle(
                fontSize: 14,
                color: Colors.white70,
              ),
            ),
            const SizedBox(height: 8),
            if (_acceptedScore != null)
              Text(
                '$_acceptedScore',
                style: const TextStyle(
                  fontSize: 34,
                  fontWeight: FontWeight.bold,
                  color: _primary,
                ),
              )
            else
              const Text(
                'Puan bilgisi alınamadı',
                textAlign: TextAlign.center,
                style: TextStyle(
                  fontSize: 14,
                  color: Colors.white60,
                ),
              ),
            const SizedBox(height: 8),
            Text(
              _isReplay
                  ? 'İlk günlük puanın korunuyor; bu tekrar denemesi liderlik tablosunu değiştirmedi.'
                  : 'Bu, liderlik tablosuna kaydedilen ilk günlük puanındır.',
              textAlign: TextAlign.center,
              style: const TextStyle(
                fontSize: 11,
                color: Colors.white54,
              ),
            ),
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
                  'İşlem tamamlanamadı',
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
              onPressed: _isSubmitting ? null : _loadTodayPuzzle,
              icon: const Icon(Icons.refresh),
              label: const Text('Tekrar Dene'),
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

void _debugLog(Object? message) {
  if (kDebugMode) debugPrint(message.toString());
}
