
import 'dart:async';
import 'dart:convert';
import 'dart:ui' show ImageFilter;

import 'package:chessground/chessground.dart';
import 'package:dartchess/dartchess.dart';
import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../../core/storage/auth_storage.dart';
import '../../../core/storage/client_id_storage.dart';
import '../../../core/storage/country_code_storage.dart';
import '../models/puzzle.dart';
import '../models/bonus_puzzle.dart';
import '../services/puzzle_api_service.dart';

class PuzzlePage extends StatefulWidget {
  const PuzzlePage({super.key});

  @override
  State<PuzzlePage> createState() => PuzzlePageState();
}

class PuzzlePageState extends State<PuzzlePage> {
  static const Color _background = Color(0xFF101A17);
  static const Color _surface = Color(0xFF1B2923);
  static const Color _primary = Color(0xFFB7D7A8);
  static const Color _secondary = Color(0xFFD6B46A);
  static const int _bonusDurationSeconds = 30;

  final PuzzleApiService _puzzleApiService = PuzzleApiService();
  final ClientIdStorage _clientIdStorage = ClientIdStorage();
  final CountryCodeStorage _countryCodeStorage =
  CountryCodeStorage();
  final AuthStorage _authStorage = AuthStorage.instance;

  // Günlük bulmaca durumu
  bool _isLoading = true;
  bool _isSolved = false;
  bool _isSubmitting = false;
  bool _hasSubmitted = false;
  bool _isReplay = false;
  bool _usedHint = false;
  bool _hasStarted = false;
  bool _isAuthenticated = false;

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

  int? _acceptedScore;

  // Bonus bulmaca durumu
  BonusPuzzle? _bonusPuzzle;

  bool _isBonusMode = false;
  bool _isBonusLoading = false;
  bool _isBonusSubmitting = false;
  bool _isBonusCompleted = false;
  bool _isBonusExpired = false;
  bool _isBonusStarted = false;
  bool _isBonusEligible = false;

  String? _bonusError;
  String? _bonusHintSquare;

  String _bonusFeedback = 'Bonus bulmaca hazırlanıyor...';

  Position? _bonusPosition;
  Position? _bonusStartingPosition;

  Move? _bonusLastMove;

  Timer? _bonusTimer;

  int _bonusRemainingSeconds = _bonusDurationSeconds;
  int _bonusWrongMoves = 0;
  int _bonusNextMoveIndex = 1;

  int? _bonusScore;

  // Canlı günlük puan
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
    final puzzle = _puzzle;

    return _hasStarted &&
        !_isLoading &&
        !_isSolved &&
        !_isSubmitting &&
        !_usedHint &&
        position != null &&
        puzzle != null &&
        position.turn == _playerSide &&
        _nextMoveIndex < puzzle.moves.length;
  }

  bool get _canUseBonusHint {
    final position = _bonusPosition;
    final puzzle = _bonusPuzzle;

    return _isBonusMode &&
        _isBonusStarted &&
        !_isBonusLoading &&
        !_isBonusSubmitting &&
        !_isBonusCompleted &&
        !_isBonusExpired &&
        _bonusRemainingSeconds > 0 &&
        position != null &&
        puzzle != null &&
        position.turn == _bonusPlayerSide &&
        _bonusNextMoveIndex < puzzle.moves.length;
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

  @override
  void dispose() {
    _timer?.cancel();
    _bonusTimer?.cancel();
    _controller?.dispose();
    super.dispose();
  }

  // ------------------------------------------------------------
  // GÜNLÜK BULMACA
  // ------------------------------------------------------------

  String _progressKey(int puzzleId) {
    return 'daily_puzzle_progress_$puzzleId';
  }

  Future<void> _saveProgress() async {
    final puzzle = _puzzle;

    if (puzzle == null ||
        !_hasStarted ||
        _hasSubmitted ||
        _isSolved) {
      return;
    }

    try {
      final preferences = await SharedPreferences.getInstance();

      final progress = {
        'nextMoveIndex': _nextMoveIndex,
        'elapsedSeconds': _elapsedSeconds,
        'wrongMoves': _wrongMoves,
        'usedHint': _usedHint,
      };

      await preferences.setString(
        _progressKey(puzzle.id),
        jsonEncode(progress),
      );
    } catch (error) {
      debugPrint('Bulmaca ilerlemesi kaydedilemedi: $error');
    }
  }

  Future<void> _clearProgress(int puzzleId) async {
    try {
      final preferences = await SharedPreferences.getInstance();

      await preferences.remove(_progressKey(puzzleId));
    } catch (error) {
      debugPrint('Bulmaca ilerleme kaydı silinemedi: $error');
    }
  }

  Future<Map<String, dynamic>?> _getSavedProgress(
      int puzzleId,
      ) async {
    try {
      final preferences = await SharedPreferences.getInstance();

      final savedData = preferences.getString(
        _progressKey(puzzleId),
      );

      if (savedData == null) return null;

      final decoded = jsonDecode(savedData);

      if (decoded is! Map<String, dynamic>) return null;

      return decoded;
    } catch (error) {
      debugPrint('Bulmaca ilerlemesi okunamadı: $error');
      return null;
    }
  }

  void _startTimer() {
    _timer?.cancel();

    _timer = Timer.periodic(
      const Duration(seconds: 1),
          (_) {
        if (!mounted || _isSolved || !_hasStarted) return;

        setState(() {
          _elapsedSeconds++;
        });

        _saveProgress();
      },
    );
  }

  void _stopTimer() {
    _timer?.cancel();
    _timer = null;
  }

  void _startPuzzle() {
    if (_hasStarted || _isLoading || _puzzle == null) return;

    setState(() {
      _hasStarted = true;
      _feedback = _hasSubmitted
          ? 'Tekrar oynuyorsun. Önceki puanın korunuyor.'
          : 'Sıra sende! Doğru hamleyi bul.';
    });

    _startTimer();
    _saveProgress();
  }

  String _formatTime(int seconds) {
    final minutes = seconds ~/ 60;
    final remainingSeconds = seconds % 60;

    return '${minutes.toString().padLeft(2, '0')}:'
        '${remainingSeconds.toString().padLeft(2, '0')}';
  }

  Future<void> _loadTodayPuzzle() async {
    if (!mounted) return;

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

      _isBonusMode = false;
      _isBonusLoading = false;
      _isBonusSubmitting = false;
      _isBonusCompleted = false;
      _isBonusExpired = false;
      _isBonusStarted = false;
      _isBonusEligible = false;

      _bonusPuzzle = null;
      _bonusError = null;
      _bonusHintSquare = null;
      _bonusScore = null;
      _bonusRemainingSeconds = _bonusDurationSeconds;

      _errorMessage = null;
      _submissionError = null;
      _hintSquare = null;

      _feedback = 'Bulmaca yükleniyor...';

      _puzzle = null;
      _position = null;
      _startingPosition = null;
      _lastMove = null;

      _bonusPosition = null;
      _bonusStartingPosition = null;
      _bonusLastMove = null;

      _nextMoveIndex = 0;
      _elapsedSeconds = 0;
      _wrongMoves = 0;
      _acceptedScore = null;
    });

    try {
      final accessToken = await _authStorage.getAccessToken();

      final isAuthenticated =
          accessToken != null && accessToken.isNotEmpty;

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

      for (final uci in puzzle.moves) {
        final move = NormalMove.fromUci(uci);

        if (!validationPosition.isLegal(move)) {
          throw Exception('Bulmacada geçersiz hamle bulundu: $uci');
        }

        validationPosition = validationPosition.play(move);
      }

      final firstMove = NormalMove.fromUci(puzzle.moves.first);
      final positionAfterOpponent = initialPosition.play(firstMove);

      Position restoredPosition = positionAfterOpponent;
      Move restoredLastMove = firstMove;

      int restoredNextMoveIndex = 1;
      int restoredElapsedSeconds = 0;
      int restoredWrongMoves = 0;
      bool restoredUsedHint = false;
      bool restoredHasStarted = false;

      if (puzzle.isSolved) {
        await _clearProgress(puzzle.id);
      } else {
        final savedProgress = await _getSavedProgress(puzzle.id);

        if (savedProgress != null) {
          try {
            final savedNextMoveIndex =
            savedProgress['nextMoveIndex'] as int;

            if (savedNextMoveIndex >= 1 &&
                savedNextMoveIndex < puzzle.moves.length) {
              var position = initialPosition;
              Move? lastMove;

              for (var i = 0; i < savedNextMoveIndex; i++) {
                final move = NormalMove.fromUci(puzzle.moves[i]);

                if (!position.isLegal(move)) {
                  throw Exception('Kaydedilmiş hamle geçersiz.');
                }

                position = position.play(move);
                lastMove = move;
              }

              restoredPosition = position;
              restoredLastMove = lastMove!;
              restoredNextMoveIndex = savedNextMoveIndex;

              restoredElapsedSeconds =
                  savedProgress['elapsedSeconds'] as int? ?? 0;

              restoredWrongMoves =
                  savedProgress['wrongMoves'] as int? ?? 0;

              restoredUsedHint =
                  savedProgress['usedHint'] as bool? ?? false;

              restoredHasStarted = true;
            } else {
              await _clearProgress(puzzle.id);
            }
          } catch (error) {
            debugPrint('Kayıtlı ilerleme geçersiz: $error');
            await _clearProgress(puzzle.id);
          }
        }
      }

      if (!mounted) return;

      _puzzle = puzzle;
      _startingPosition = positionAfterOpponent;
      _position = restoredPosition;
      _lastMove = restoredLastMove;
      _nextMoveIndex = restoredNextMoveIndex;

      _elapsedSeconds = restoredElapsedSeconds;
      _wrongMoves = restoredWrongMoves;
      _usedHint = restoredUsedHint;

      _controller = ChessboardController(
        game: _buildGame(restoredPosition),
      );

      setState(() {
        _isLoading = false;
        _isAuthenticated = isAuthenticated;

        _hasSubmitted = puzzle.isSolved;
        _isReplay = puzzle.isSolved;

        _acceptedScore = isAuthenticated
            ? puzzle.previousScore
            : null;

        _isSolved = false;
        _hasStarted = restoredHasStarted;

        _feedback = restoredHasStarted
            ? 'Kaldığın yerden devam ediyorsun!'
            : _hasSubmitted
            ? 'Bu bulmacayı daha önce çözdün. Tekrar oynayabilirsin.'
            : 'Hazır olduğunda bulmacaya başlayabilirsin.';
      });

      if (restoredHasStarted) {
        _startTimer();
      }

      if (isAuthenticated && puzzle.isSolved) {
        await _loadBonusStatus();
      }
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

  void _onMove(Move move, {bool? viaDragAndDrop}) {
    if (_isBonusMode) {
      unawaited(_onBonusMove(move));
      return;
    }

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

      _saveProgress();
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

    _saveProgress();

    if (_nextMoveIndex >= puzzle.moves.length) {
      _finishPuzzle();
      return;
    }

    setState(() {
      _feedback = 'Doğru hamle! Rakibin cevabı bekleniyor...';
      _hintSquare = null;
    });

    if (newPosition.turn != _playerSide) {
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

    _saveProgress();

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

    _saveProgress();
  }

  Future<void> _finishPuzzle() async {
    if (!mounted || _isSolved) return;

    _stopTimer();

    final puzzleId = _puzzle?.id;

    setState(() {
      _isSolved = true;
      _feedback = 'Tebrikler! Bulmacayı başarıyla çözdün.';
    });

    if (puzzleId != null) {
      await _clearProgress(puzzleId);
    }

    if (!mounted) return;

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
      final countryCode =
      await _countryCodeStorage.getCountryCode();

      final response = await _puzzleApiService.submitPuzzle(
        request: {
          'clientId': clientId,
          'puzzleId': puzzle.id,
          'seconds': _elapsedSeconds,
          'wrongMoves': _wrongMoves,
          'usedHint': _usedHint,
          'countryCode': countryCode,
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

      if (!mounted) return;

      setState(() {
        _acceptedScore = _isAuthenticated
            ? receivedScore
            : null;

        _hasSubmitted = true;
        _isReplay = false;
        _isSubmitting = false;

        _feedback = receivedScore != null
            ? 'Tebrikler! Bulmaca sonucunuz kaydedildi.'
            : 'Sonucunuz kaydedildi ancak puan bilgisi alınamadı.';
      });

      if (_isAuthenticated) {
        await _loadBonusStatus();
      }
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

  void _restartPuzzle() {
    final startingPosition = _startingPosition;
    final puzzle = _puzzle;

    if (startingPosition == null ||
        puzzle == null ||
        _isSubmitting) {
      return;
    }

    _stopTimer();
    _clearProgress(puzzle.id);

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
  }

  // ------------------------------------------------------------
  // BONUS BULMACA
  // ------------------------------------------------------------

  Future<void> _loadBonusStatus() async {
    if (!_isAuthenticated || _isBonusLoading || !mounted) {
      debugPrint(
        'BONUS SKIPPED: '
            'authenticated=$_isAuthenticated, '
            'loading=$_isBonusLoading, '
            'mounted=$mounted',
      );
      return;
    }

    setState(() {
      _isBonusLoading = true;
    });

    try {
      debugPrint('BONUS REQUEST: GET /api/puzzle/bonus');

      final response = await _puzzleApiService.getBonusPuzzle();

      debugPrint('BONUS RESPONSE:');
      debugPrint('Success: ${response.success}');
      debugPrint('Status: ${response.status}');
      debugPrint('Message: ${response.message}');
      debugPrint('ErrorCode: ${response.errorCode}');
      debugPrint('Data exists: ${response.data != null}');
      debugPrint('IsEligible: ${response.data?.isEligible}');
      debugPrint('PuzzleId: ${response.data?.puzzleId}');

      if (!mounted) return;

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
        _isBonusLoading = false;
        _bonusError = null;
      });

      debugPrint(
        'BONUS STATE UPDATED: eligible=$_isBonusEligible',
      );
    } catch (e, stackTrace) {
      debugPrint('BONUS EXCEPTION: $e');
      debugPrint('$stackTrace');

      if (!mounted) return;

      setState(() {
        _isBonusLoading = false;
        _isBonusEligible = false;
        _bonusError = e.toString();
      });
    }
  }

  // Bonus kartından buraya gelindiğinde yalnızca bulmaca yüklenir.
  // Sunucuda bonus oturumu başlatılmaz ve süre başlamaz.
  Future<void> _startBonusPuzzle() async {
    if (!_isAuthenticated ||
        !_isBonusEligible ||
        _isBonusLoading ||
        _isBonusStarted ||
        _isBonusCompleted ||
        _isBonusExpired) {
      return;
    }

    setState(() {
      _isBonusLoading = true;
      _bonusError = null;
      _bonusFeedback = 'Bonus bulmaca hazırlanıyor...';
    });

    try {
      final response =
      await _puzzleApiService.getBonusPuzzle();

      if (!mounted) return;

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
          _bonusRemainingSeconds = 0;
          _isBonusLoading = false;
        });
        return;
      }

      // Sunucuda daha önce başlatılmışsa yeni bir tahta kurma.
      if (bonus.startedAtUtc != null) {
        setState(() {
          _bonusPuzzle = bonus;
          _isBonusStarted = true;
          _bonusRemainingSeconds =
              bonus.remainingSeconds ?? 0;
          _isBonusLoading = false;
          _bonusFeedback =
          'Bonus denemen daha önce başlatılmış. Süre işlemeye devam ediyor.';
        });
        return;
      }

      if (bonus.fen == null || bonus.moves.isEmpty) {
        throw Exception('Bonus bulmaca verileri eksik.');
      }

      final initialPosition = Chess.fromSetup(
        Setup.parseFen(bonus.fen!),
      );

      Position validationPosition = initialPosition;

      for (final uci in bonus.moves) {
        final move = NormalMove.fromUci(uci);

        if (!validationPosition.isLegal(move)) {
          throw Exception(
            'Bonus bulmacada geçersiz hamle: $uci',
          );
        }

        validationPosition = validationPosition.play(move);
      }

      // İlk rakip hamlesi otomatik uygulanır.
      final firstMove = NormalMove.fromUci(
        bonus.moves.first,
      );

      final positionAfterOpponent =
      initialPosition.play(firstMove);

      _stopBonusTimer();
      _controller?.dispose();

      _bonusPuzzle = bonus;
      _bonusStartingPosition = positionAfterOpponent;
      _bonusPosition = positionAfterOpponent;
      _bonusLastMove = firstMove;

      _bonusNextMoveIndex = 1;
      _bonusWrongMoves = 0;
      _bonusRemainingSeconds =
          bonus.remainingSeconds ?? _bonusDurationSeconds;

      _bonusScore = bonus.score;
      _bonusHintSquare = null;

      _isBonusStarted = false;
      _isBonusCompleted = false;
      _isBonusExpired = false;

      _controller = ChessboardController(
        game: _buildBonusGame(positionAfterOpponent),
      );

      setState(() {
        _isBonusMode = true;
        _isBonusLoading = false;

        _bonusFeedback =
        'Hazır olduğunda başla. 30 saniyelik süren butona bastığında başlayacak.';
      });

      // Henüz başlamadığı için bonus sayacı çalışmaz.
      _stopBonusTimer();
    } catch (error, stackTrace) {
      debugPrint('Bonus hazırlama hatası: $error');
      debugPrint('$stackTrace');

      if (!mounted) return;

      setState(() {
        _isBonusLoading = false;
        _bonusError = error.toString();
      });
    }
  }

  // Bonus başlangıç butonuna basıldığında çağrılır.
  // Süre yalnızca sunucu başlangıcı onayladıktan sonra başlar.
  Future<void> _beginBonusPuzzle() async {
    if (!_isBonusMode ||
        _isBonusStarted ||
        _isBonusLoading ||
        _isBonusCompleted ||
        _isBonusExpired) {
      return;
    }

    setState(() {
      _isBonusLoading = true;
      _bonusError = null;
      _bonusFeedback = 'Bonus bulmaca başlatılıyor...';
    });

    try {
      final response =
      await _puzzleApiService.startBonusPuzzle();

      if (!mounted) return;

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

      setState(() {
        _bonusPuzzle = bonus;
        _isBonusStarted = true;
        _bonusRemainingSeconds =
            bonus.remainingSeconds ?? _bonusDurationSeconds;
        _isBonusLoading = false;
        _bonusFeedback = '30 saniyen başladı! Doğru hamleyi bul.';
      });

      _startBonusTimer();
    } catch (error, stackTrace) {
      debugPrint('Bonus başlatma hatası: $error');
      debugPrint('$stackTrace');

      if (!mounted) return;

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

    _bonusTimer = Timer.periodic(
      const Duration(seconds: 1),
          (_) {
        if (!mounted ||
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
    if (!mounted || !_isBonusMode) return;

    try {
      final response =
      await _puzzleApiService.getBonusPuzzle();

      if (!mounted) return;

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
        _bonusRemainingSeconds =
            bonus.remainingSeconds ?? 0;
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
      debugPrint('Bonus süre kontrol hatası: $error');
    }
  }

  Future<void> _onBonusMove(Move move) async {
    if (!_isBonusMode ||
        !_isBonusStarted ||
        _isBonusLoading ||
        _isBonusSubmitting ||
        _isBonusCompleted ||
        _isBonusExpired ||
        _bonusRemainingSeconds <= 0) {
      return;
    }

    final position = _bonusPosition;
    final bonus = _bonusPuzzle;

    if (position == null || bonus == null) return;

    if (!position.isLegal(move)) {
      _restoreBonusBoard();
      return;
    }

    if (_bonusNextMoveIndex >= bonus.moves.length) {
      _restoreBonusBoard();
      return;
    }

    setState(() {
      _isBonusLoading = true;
    });

    try {
      final response = await _puzzleApiService.submitBonusMove(
        uci: move.uci,
      );

      if (!mounted) return;

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

      final serverMoveIndex =
          _readInt(data, 'currentMoveIndex') ??
              _readInt(data, 'CurrentMoveIndex') ??
              _bonusNextMoveIndex;

      final serverWrongMoves =
          _readInt(data, 'wrongMoves') ??
              _readInt(data, 'WrongMoves') ??
              _bonusWrongMoves;

      final serverCompleted =
          data['isCompleted'] == true ||
              data['IsCompleted'] == true;

      if (serverMoveIndex <= _bonusNextMoveIndex) {
        setState(() {
          _isBonusLoading = false;
          _bonusWrongMoves = serverWrongMoves;
          _bonusFeedback = 'Yanlış hamle. Tekrar dene!';
        });

        _restoreBonusBoard();
        return;
      }

      final newPosition = position.play(move);

      _bonusPosition = newPosition;
      _bonusLastMove = move;
      _bonusNextMoveIndex++;

      setState(() {
        _isBonusLoading = false;
        _bonusWrongMoves = serverWrongMoves;
        _bonusHintSquare = null;
        _bonusFeedback = 'Doğru hamle!';
      });

      _controller?.updatePosition(
        _buildBonusGame(newPosition),
      );

      if (serverCompleted ||
          _bonusNextMoveIndex >= bonus.moves.length) {
        await _finishBonusPuzzle();
        return;
      }

      if (newPosition.turn != _bonusPlayerSide) {
        await _playBonusOpponentMove();
      }
    } catch (error, stackTrace) {
      debugPrint('Bonus hamle hatası: $error');
      debugPrint('$stackTrace');

      if (!mounted) return;

      setState(() {
        _isBonusLoading = false;
        _bonusFeedback =
        'Hamle gönderilemedi. Tekrar deneyebilirsin.';
      });

      _restoreBonusBoard();
    }
  }

  Future<void> _playBonusOpponentMove() async {
    final bonus = _bonusPuzzle;

    if (bonus == null) return;

    await Future.delayed(
      const Duration(milliseconds: 500),
    );

    if (!mounted ||
        !_isBonusMode ||
        _isBonusCompleted ||
        _isBonusExpired) {
      return;
    }

    final position = _bonusPosition;

    if (position == null) return;

    if (_bonusNextMoveIndex >= bonus.moves.length) {
      await _finishBonusPuzzle();
      return;
    }

    final uci = bonus.moves[_bonusNextMoveIndex];
    final move = NormalMove.fromUci(uci);

    if (!position.isLegal(move)) {
      setState(() {
        _bonusFeedback = 'Rakibin hamlesi geçersiz: $uci';
      });
      return;
    }

    final newPosition = position.play(move);

    _bonusPosition = newPosition;
    _bonusLastMove = move;
    _bonusNextMoveIndex++;

    _controller?.updatePosition(
      _buildBonusGame(newPosition),
    );

    if (_bonusNextMoveIndex >= bonus.moves.length) {
      await _finishBonusPuzzle();
      return;
    }

    if (!mounted) return;

    setState(() {
      _bonusFeedback = 'Sıra sende! Hamleni yap.';
    });
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

    final bonus = _bonusPuzzle!;

    if (_bonusNextMoveIndex >= bonus.moves.length) return;

    setState(() {
      _isBonusLoading = true;
    });

    try {
      final response =
      await _puzzleApiService.useBonusHint();

      if (!mounted) return;

      if (!response.success) {
        throw Exception(
          response.message ?? 'Bonus ipucu kullanılamadı.',
        );
      }

      final expectedMove = bonus.moves[_bonusNextMoveIndex];

      if (expectedMove.length < 4) return;

      final fromSquare = expectedMove.substring(0, 2);

      setState(() {
        _isBonusLoading = false;
        _bonusHintSquare = fromSquare;
        _bonusFeedback =
        'İpucu: $fromSquare karesindeki taşı hareket ettir.';
      });
    } catch (error) {
      debugPrint('Bonus ipucu hatası: $error');

      if (!mounted) return;

      setState(() {
        _isBonusLoading = false;
        _bonusFeedback = error.toString();
      });
    }
  }

  Future<void> _finishBonusPuzzle() async {
    if (!mounted || _isBonusSubmitting || _isBonusCompleted) {
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

      if (!mounted) return;

      if (!response.success) {
        throw Exception(
          response.message ?? 'Bonus sonucu kaydedilemedi.',
        );
      }

      final data = response.data;

      final score = _readScore(data);

      setState(() {
        _bonusScore = score;
        _isBonusCompleted = true;
        _isBonusExpired = false;
        _isBonusSubmitting = false;
        _bonusRemainingSeconds = 0;

        _bonusFeedback = score != null
            ? 'Tebrikler! Bonus puanın kaydedildi.'
            : 'Bonus tamamlandı.';
      });
    } catch (error, stackTrace) {
      debugPrint('Bonus sonuç kaydetme hatası: $error');
      debugPrint('$stackTrace');

      if (!mounted) return;

      setState(() {
        _isBonusSubmitting = false;
        _bonusFeedback =
        'Bonus çözüldü ancak sonuç kaydedilemedi: $error';
      });
    }
  }

  int? _readInt(Map<String, dynamic> data, String key) {
    final value = data[key];

    if (value is num) return value.toInt();
    if (value == null) return null;

    return int.tryParse(value.toString());
  }

  void _returnToDailyPuzzle() {
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

    // Bonus hakkı ve süresi sunucudan tekrar kontrol edilir.
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
      if (_isBonusCompleted || _isBonusExpired) {
        return _buildBonusResultBody();
      }

      return _buildBonusPuzzleBody();
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
              Column(
                children: [
                  Padding(
                    padding: const EdgeInsets.fromLTRB(
                      16,
                      12,
                      16,
                      0,
                    ),
                    child: _buildPuzzleInfo(),
                  ),
                  const SizedBox(height: 12),
                  Center(
                    child: SizedBox(
                      width: boardSize,
                      height: boardSize,
                      child: Chessboard(
                        controller: _controller!,
                        size: boardSize,
                        orientation: _playerSide,
                        onMove: _onMove,
                      ),
                    ),
                  ),
                  const SizedBox(height: 12),
                  Expanded(
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
                            4,
                            16,
                            28,
                          ),
                          child: Column(
                            crossAxisAlignment:
                            CrossAxisAlignment.stretch,
                            children: [
                              // Bonus kartı günlük ekranda da görünür.
                              if (_isAuthenticated &&
                                  _isBonusEligible) ...[
                                _buildBonusEntryCard(),
                                const SizedBox(height: 16),
                              ],

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
                    ),
                  ),
                ],
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

  Widget _buildBonusPuzzleBody() {
    final position = _bonusPosition;
    final controller = _controller;

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
                    padding: const EdgeInsets.fromLTRB(
                      16,
                      12,
                      16,
                      0,
                    ),
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
                              crossAxisAlignment:
                              CrossAxisAlignment.start,
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
                                  'Ek puan kazanmak için 30 saniyen var.',
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
                      child: Chessboard(
                        controller: controller,
                        size: boardSize,
                        orientation: _bonusPlayerSide,
                        onMove: _onMove,
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
                          padding: const EdgeInsets.fromLTRB(
                            16,
                            4,
                            16,
                            24,
                          ),
                          child: Column(
                            crossAxisAlignment:
                            CrossAxisAlignment.stretch,
                            children: [
                              Row(
                                children: [
                                  Expanded(
                                    child: _buildMiniStatistic(
                                      icon: Icons.timer_outlined,
                                      label: 'Kalan süre',
                                      value:
                                      '$_bonusRemainingSeconds sn',
                                      color: _secondary,
                                    ),
                                  ),
                                  const SizedBox(width: 12),
                                  Expanded(
                                    child: _buildMiniStatistic(
                                      icon: Icons.close_rounded,
                                      label: 'Yanlış hamle',
                                      value: '$_bonusWrongMoves',
                                      color:
                                      const Color(0xFFE69A91),
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
                                        foregroundColor:
                                        Colors.white70,
                                        padding:
                                        const EdgeInsets.symmetric(
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
                                      icon: const Icon(
                                        Icons.lightbulb_outline,
                                      ),
                                      label: const Text('İpucu Al'),
                                      style: ElevatedButton.styleFrom(
                                        backgroundColor: _secondary,
                                        foregroundColor: _background,
                                        padding:
                                        const EdgeInsets.symmetric(
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
                  '30 saniyelik süren yalnızca başla butonuna '
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
                onPressed:
                _isBonusLoading ? null : _beginBonusPuzzle,
                icon: _isBonusLoading
                    ? const SizedBox(
                  width: 19,
                  height: 19,
                  child: CircularProgressIndicator(
                    strokeWidth: 2,
                    color: _background,
                  ),
                )
                    : const Icon(
                  Icons.play_arrow_rounded,
                  size: 27,
                ),
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
                onPressed: _isBonusLoading
                    ? null
                    : _returnToDailyPuzzle,
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
    final isWrong =
    _bonusFeedback.toLowerCase().contains('yanlış');
    final color = isWrong
        ? const Color(0xFFE69A91)
        : _primary;

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
                : Icons.info_outline,
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
          const Icon(
            Icons.lightbulb,
            color: _secondary,
          ),
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
            color: expired
                ? const Color(0xFFE69A91)
                : _secondary,
          ),
          const SizedBox(height: 20),
          Text(
            expired
                ? 'Bonus Süresi Doldu'
                : 'Bonus Tamamlandı!',
            style: const TextStyle(
              fontSize: 25,
              fontWeight: FontWeight.bold,
            ),
          ),
          const SizedBox(height: 12),
          Text(
            expired
                ? '30 saniyelik bonus süresini tamamlayamadın.'
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
                icon: const Icon(
                  Icons.play_arrow_rounded,
                  size: 27,
                ),
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
          if (_isAuthenticated && _isBonusEligible) ...[
            const SizedBox(height: 22),
            _buildBonusEntryCard(),
          ],
          if (_isAuthenticated &&
              _isBonusLoading &&
              !_isBonusEligible) ...[
            const SizedBox(height: 18),
            const Center(
              child: CircularProgressIndicator(color: _primary),
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
      'Hatasız çözümün sayesinde 30 saniyelik bonus bulmaca '
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
                onPressed: _isBonusLoading
                    ? null
                    : _startBonusPuzzle,
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
            'Canlı puanın',
            style: TextStyle(
              fontSize: 15,
              color: Colors.white70,
            ),
          ),
          const SizedBox(height: 8),
          Text(
            '$_liveScore',
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
              'Sunucuda kayıtlı günlük puanın',
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
              Text(
                _isSubmitting
                    ? 'Kaydediliyor...'
                    : _hasSubmitted
                    ? 'Puan bilgisi alınamadı'
                    : _submissionError != null
                    ? 'Kaydedilemedi'
                    : 'Henüz kaydedilmedi',
                textAlign: TextAlign.center,
                style: const TextStyle(
                  fontSize: 14,
                  color: Colors.white60,
                ),
              ),
            const SizedBox(height: 8),
            Text(
              _isReplay
                  ? 'Önceki kayıtlı puanın korunuyor.'
                  : _acceptedScore != null
                  ? 'Bu puan sunucudan alınmıştır.'
                  : 'Kayıt durumu sunucu yanıtına göre güncellenecek.',
              textAlign: TextAlign.center,
              style: const TextStyle(
                fontSize: 11,
                color: Colors.white54,
              ),
            ),
          ],
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
