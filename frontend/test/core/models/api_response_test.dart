import 'package:daily_chess_puzzle/core/models/api_response.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('localizes known API error codes and retains metadata', () {
    final response = ApiResponse<void>.fromJson({
      'success': false,
      'status': 401,
      'errorCode': 'INVALID_CREDENTIALS',
      'message': 'Raw server message',
      'traceId': 'trace-123',
      'timestamp': '2026-11-20T10:00:00Z',
      'errors': ['detail'],
    });

    expect(response.success, isFalse);
    expect(response.status, 401);
    expect(response.errorCode, 'INVALID_CREDENTIALS');
    expect(response.message, 'E-posta veya şifre hatalı.');
    expect(response.traceId, 'trace-123');
    expect(response.timestamp, DateTime.utc(2026, 11, 20, 10));
    expect(response.errors, ['detail']);
  });

  test('uses server message for unknown codes and supports typed data', () {
    final response = ApiResponse<int>.fromJson(
      {
        'success': true,
        'message': 'ok',
        'data': {'value': 42},
      },
      fromJsonT: (json) => (json as Map<String, dynamic>)['value'] as int,
    );

    expect(response.message, 'ok');
    expect(response.data, 42);
  });
}
