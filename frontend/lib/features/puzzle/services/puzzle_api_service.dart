
import 'package:dio/dio.dart';

import '../../../core/constants/api_constants.dart';
import '../../../core/models/api_response.dart';
import '../../../core/network/api_client.dart';
import '../models/puzzle.dart';
import '../models/bonus_puzzle.dart';

class PuzzleApiService {
  final Dio _dio = ApiClient.dio;

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

  // Günlük bulmaca sonucunu gönder.
  Future<ApiResponse<dynamic>> submitPuzzle({
    required Map<String, dynamic> request,
  }) async {
    final response = await _dio.post(
      ApiConstants.submitPuzzleEndpoint,
      data: request,
    );

    return ApiResponse<dynamic>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
    );
  }

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
  Future<ApiResponse<Map<String, dynamic>>> submitBonusMove({
    required String uci,
    String? clientId,
  }) async {
    final request = <String, dynamic>{
      'uci': uci,
    };

    if (clientId != null) {
      request['clientId'] = clientId;
    }

    final response = await _dio.post(
      ApiConstants.submitBonusMoveEndpoint,
      data: request,
    );

    return ApiResponse<Map<String, dynamic>>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => Map<String, dynamic>.from(json as Map),
    );
  }

  // Bonus bulmacada ipucu kullan.
  Future<ApiResponse<Map<String, dynamic>>> useBonusHint() async {
    final response = await _dio.post(
      ApiConstants.useBonusHintEndpoint,
    );

    return ApiResponse<Map<String, dynamic>>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => Map<String, dynamic>.from(json as Map),
    );
  }

  // Bonus bulmaca sonucunu gönder.
  Future<ApiResponse<Map<String, dynamic>>> submitBonusPuzzle() async {
    final response = await _dio.post(
      ApiConstants.submitBonusPuzzleEndpoint,
    );

    return ApiResponse<Map<String, dynamic>>.fromJson(
      Map<String, dynamic>.from(response.data as Map),
      fromJsonT: (json) => Map<String, dynamic>.from(json as Map),
    );
  }
}
