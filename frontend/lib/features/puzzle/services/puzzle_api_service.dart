
import 'package:dio/dio.dart';

import '../../../core/constants/api_constants.dart';
import '../../../core/models/api_response.dart';
import '../../../core/network/api_client.dart';
import '../models/puzzle.dart';
import '../models/bonus_puzzle.dart';
import '../models/puzzle_session.dart';
import '../models/puzzle_action_result.dart';

class PuzzleApiService {
  PuzzleApiService({Dio? dio}) : _dio = dio ?? ApiClient.dio;

  final Dio _dio;

  // ---------------------------------------------------------
  // GÜNLÜK BULMACA
  // ---------------------------------------------------------

  // Günlük bulmacayı getir.
  Future<ApiResponse<Puzzle>> getTodayPuzzle({
    required String clientId,
  }) async {
    final response = await _dio.get(
      ApiConstants.todayPuzzleEndpoint,
      queryParameters: {
        'clientId': clientId,
      },
    );

    return ApiResponse<Puzzle>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => Puzzle.fromJson(
        Map<String, dynamic>.from(json as Map),
      ),
    );
  }

  // Yeni bir günlük bulmaca oturumu başlat.
  Future<ApiResponse<PuzzleSession>> startPuzzleSession({
    required String clientId,
  }) async {
    final response = await _dio.post(
      ApiConstants.startPuzzleSessionEndpoint,
      queryParameters: {
        'clientId': clientId,
      },
    );

    return ApiResponse<PuzzleSession>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => PuzzleSession.fromJson(
        Map<String, dynamic>.from(json as Map),
      ),
    );
  }

  // ---------------------------------------------------------
  // HAMLE VE İPUCU
  // ---------------------------------------------------------

  // Belirli bir günlük oturuma hamle gönder.
  Future<ApiResponse<PuzzleActionResult>> submitPuzzleMove({
    required int sessionId,
    required String uci,
    required String clientId,
    required int expectedMoveIndex,
  }) async {
    final response = await _dio.post(
      ApiConstants.submitPuzzleMoveEndpoint,
      data: {
        'sessionId': sessionId,
        'uci': uci,
        'clientId': clientId,
        'expectedMoveIndex': expectedMoveIndex,
      },
    );

    return _parseActionResponse(response);
  }

  // Belirli bir günlük oturumda ipucu kullan.
  Future<ApiResponse<PuzzleActionResult>> usePuzzleHint({
    required int sessionId,
    required String clientId,
  }) async {
    final response = await _dio.post(
      ApiConstants.usePuzzleHintEndpoint,
      queryParameters: {
        'sessionId': sessionId,
        'clientId': clientId,
      },
    );

    return _parseActionResponse(response);
  }

  // ---------------------------------------------------------
  // BONUS BULMACA
  // ---------------------------------------------------------

  // Bonus bulmacanın durumunu getir.
  Future<ApiResponse<BonusPuzzle>> getBonusPuzzle() async {
    final response = await _dio.get(
      ApiConstants.bonusPuzzleEndpoint,
    );

    return ApiResponse<BonusPuzzle>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => BonusPuzzle.fromJson(
        Map<String, dynamic>.from(json as Map),
      ),
    );
  }

  // Bonus bulmacayı başlat.
  Future<ApiResponse<BonusPuzzle>> startBonusPuzzle() async {
    final response = await _dio.post(
      ApiConstants.startBonusPuzzleEndpoint,
    );

    return ApiResponse<BonusPuzzle>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => BonusPuzzle.fromJson(
        Map<String, dynamic>.from(json as Map),
      ),
    );
  }

  // Bonus bulmacaya hamle gönder.
  Future<ApiResponse<PuzzleActionResult>> submitBonusMove({
    required String uci,
    required int expectedMoveIndex,
  }) async {
    final response = await _dio.post(
      ApiConstants.submitBonusMoveEndpoint,
      data: {
        'uci': uci,
        'expectedMoveIndex': expectedMoveIndex,
      },
    );

    return _parseActionResponse(response);
  }

  // Bonus bulmacada ipucu kullan.
  Future<ApiResponse<PuzzleActionResult>> useBonusHint() async {
    final response = await _dio.post(
      ApiConstants.useBonusHintEndpoint,
    );

    return _parseActionResponse(response);
  }

  // Bonus bulmaca sonucunu kaydet.
  Future<ApiResponse<PuzzleActionResult>> submitBonusPuzzle() async {
    final response = await _dio.post(
      ApiConstants.submitBonusPuzzleEndpoint,
    );

    return _parseActionResponse(response);
  }

  // ---------------------------------------------------------
  // ORTAK RESPONSE PARSER
  // ---------------------------------------------------------

  ApiResponse<PuzzleActionResult> _parseActionResponse(
      Response response,
      ) {
    return ApiResponse<PuzzleActionResult>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => PuzzleActionResult.fromJson(
        Map<String, dynamic>.from(json as Map),
      ),
    );
  }
}
