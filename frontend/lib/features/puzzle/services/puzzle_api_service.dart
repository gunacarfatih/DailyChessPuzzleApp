
import 'package:dio/dio.dart';

import '../../../core/constants/api_constants.dart';
import '../../../core/models/api_response.dart';
import '../../../core/network/api_client.dart';
import '../models/puzzle.dart';

class PuzzleApiService {
  final Dio _dio = ApiClient.dio;

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
}
