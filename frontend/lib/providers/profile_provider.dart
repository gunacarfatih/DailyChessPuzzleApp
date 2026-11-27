import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

import '../core/network/api_client.dart';

class ProfileProvider extends ChangeNotifier {
  Map<String, dynamic>? summary;
  List<Map<String, dynamic>> history = const [];
  bool loading = false;
  String? error;

  void clear() {
    summary = null;
    history = const [];
    error = null;
    loading = false;
    notifyListeners();
  }

  Map<String, dynamic> _data(dynamic value) {
    final map = Map<String, dynamic>.from(value as Map);
    return Map<String, dynamic>.from(map['data'] ?? map);
  }

  Future<void> refresh() async {
    loading = true;
    error = null;
    notifyListeners();
    try {
      final result = await ApiClient.dio.get('/api/profile');
      summary = _data(result.data);
    } on DioException catch (e) {
      error = e.message ?? 'Profil yüklenemedi.';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> loadHistory() async {
    loading = true;
    error = null;
    notifyListeners();
    try {
      final result = await ApiClient.dio.get(
        '/api/profile/history',
        queryParameters: {'page': 1, 'pageSize': 50},
      );
      final data = _data(result.data);
      history = ((data['items'] as List?) ?? const [])
          .map((item) => Map<String, dynamic>.from(item as Map))
          .toList(growable: false);
    } on DioException catch (e) {
      error = e.message ?? 'Çözüm geçmişi yüklenemedi.';
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<Map<String, dynamic>> watch(int attemptId) async {
    final result = await ApiClient.dio.get(
      '/api/profile/history/$attemptId/watch',
    );
    return _data(result.data);
  }
}
