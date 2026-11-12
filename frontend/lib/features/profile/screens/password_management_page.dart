
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../auth/services/auth_api_service.dart';

class PasswordManagementPage extends StatefulWidget {
  final bool hasPassword;

  const PasswordManagementPage({
    super.key,
    required this.hasPassword,
  });

  @override
  State<PasswordManagementPage> createState() =>
      _PasswordManagementPageState();
}

class _PasswordManagementPageState
    extends State<PasswordManagementPage> {
  final AuthApiService _authApiService = AuthApiService();

  final TextEditingController _currentPasswordController =
  TextEditingController();

  final TextEditingController _newPasswordController =
  TextEditingController();

  final TextEditingController _confirmPasswordController =
  TextEditingController();

  bool _isLoading = false;
  bool _obscureCurrentPassword = true;
  bool _obscureNewPassword = true;
  bool _obscureConfirmPassword = true;

  @override
  void dispose() {
    _currentPasswordController.dispose();
    _newPasswordController.dispose();
    _confirmPasswordController.dispose();
    super.dispose();
  }

  String get _pageTitle =>
      widget.hasPassword ? 'Şifre Değiştir' : 'Şifre Belirle';

  Future<void> _submit() async {
    final currentPassword = _currentPasswordController.text;
    final newPassword = _newPasswordController.text;
    final confirmPassword = _confirmPasswordController.text;

    if (widget.hasPassword && currentPassword.isEmpty) {
      _showMessage('Mevcut şifreni girmelisin.');
      return;
    }

    if (newPassword.length < 8 || newPassword.length > 128) {
      _showMessage('Yeni şifre 8-128 karakter arasında olmalıdır.');
      return;
    }

    if (confirmPassword != newPassword) {
      _showMessage('Yeni şifreler birbiriyle eşleşmiyor.');
      return;
    }

    if (widget.hasPassword && currentPassword == newPassword) {
      _showMessage('Yeni şifren mevcut şifrenle aynı olamaz.');
      return;
    }

    setState(() {
      _isLoading = true;
    });

    try {
      final result = widget.hasPassword
          ? await _authApiService.changePassword(
        currentPassword: currentPassword,
        newPassword: newPassword,
      )
          : await _authApiService.setPassword(
        newPassword: newPassword,
      );

      if (!mounted) return;

      if (!result.success) {
        _showMessage(
          result.message ?? 'Şifre işlemi gerçekleştirilemedi.',
        );
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            widget.hasPassword
                ? 'Şifren başarıyla değiştirildi.'
                : 'Şifren başarıyla oluşturuldu.',
          ),
          backgroundColor: Colors.green,
        ),
      );

      Navigator.pop(context, true);
    } on DioException catch (error) {
      if (!mounted) return;

      _showMessage(_getDioErrorMessage(error));
    } catch (error) {
      if (!mounted) return;

      _showMessage('Şifre işlemi sırasında bir hata oluştu.');
    } finally {
      if (mounted) {
        setState(() {
          _isLoading = false;
        });
      }
    }
  }

  String _getDioErrorMessage(DioException error) {
    final responseData = error.response?.data;

    if (responseData is Map) {
      final message = responseData['message']?.toString();

      if (message != null && message.isNotEmpty) {
        return _translateErrorMessage(message);
      }

      final errors = responseData['errors'];

      if (errors is List && errors.isNotEmpty) {
        return errors.first.toString();
      }
    }

    if (error.response?.statusCode == 401) {
      return 'Mevcut şifren hatalı veya oturumun geçersiz.';
    }

    if (error.response?.statusCode == 400) {
      return 'Gönderilen bilgiler geçersiz. Lütfen kontrol et.';
    }

    return 'Sunucuya ulaşılamadı. Lütfen tekrar dene.';
  }

  String _translateErrorMessage(String message) {
    switch (message) {
      case 'INVALID_CURRENT_PASSWORD':
        return 'Mevcut şifren hatalı.';
      case 'PASSWORD_ALREADY_SET':
        return 'Hesabında zaten bir şifre bulunuyor.';
      case 'PASSWORD_NOT_SET':
        return 'Henüz hesabına bir şifre belirlenmemiş.';
      case 'USER_NOT_FOUND':
        return 'Kullanıcı hesabı bulunamadı.';
      case 'INVALID_AUTHENTICATED_USER':
        return 'Oturum bilgileri geçersiz. Tekrar giriş yap.';
      default:
        return message;
    }
  }

  void _showMessage(String message) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(message),
        backgroundColor: Colors.red,
      ),
    );
  }

  Widget _buildPasswordField({
    required TextEditingController controller,
    required String label,
    required bool obscureText,
    required VoidCallback onVisibilityChanged,
  }) {
    return TextField(
      controller: controller,
      obscureText: obscureText,
      enabled: !_isLoading,
      textInputAction: TextInputAction.next,
      decoration: InputDecoration(
        labelText: label,
        prefixIcon: const Icon(Icons.lock_outline),
        suffixIcon: IconButton(
          onPressed: onVisibilityChanged,
          icon: Icon(
            obscureText ? Icons.visibility : Icons.visibility_off,
          ),
        ),
        filled: true,
        fillColor: const Color(0xFF1B2923),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(14),
          borderSide: BorderSide.none,
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(
          _pageTitle,
          style: const TextStyle(
            fontWeight: FontWeight.bold,
          ),
        ),
      ),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(22),
          children: [
            Container(
              padding: const EdgeInsets.all(22),
              decoration: BoxDecoration(
                color: const Color(0xFF26382D),
                borderRadius: BorderRadius.circular(18),
              ),
              child: Column(
                children: [
                  const Icon(
                    Icons.lock_reset,
                    size: 52,
                    color: Color(0xFFB7D7A8),
                  ),
                  const SizedBox(height: 14),
                  Text(
                    _pageTitle,
                    style: const TextStyle(
                      fontSize: 21,
                      fontWeight: FontWeight.bold,
                    ),
                  ),
                  const SizedBox(height: 8),
                  Text(
                    widget.hasPassword
                        ? 'Hesabının güvenliği için önce mevcut şifreni doğrula.'
                        : 'Hesabına e-posta ve şifre ile de giriş yapabilmek için bir şifre oluştur.',
                    textAlign: TextAlign.center,
                    style: const TextStyle(
                      color: Colors.white70,
                      fontSize: 13,
                      height: 1.5,
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 26),

            if (widget.hasPassword) ...[
              _buildPasswordField(
                controller: _currentPasswordController,
                label: 'Mevcut şifre',
                obscureText: _obscureCurrentPassword,
                onVisibilityChanged: () {
                  setState(() {
                    _obscureCurrentPassword =
                    !_obscureCurrentPassword;
                  });
                },
              ),
              const SizedBox(height: 16),
            ],

            _buildPasswordField(
              controller: _newPasswordController,
              label: 'Yeni şifre',
              obscureText: _obscureNewPassword,
              onVisibilityChanged: () {
                setState(() {
                  _obscureNewPassword = !_obscureNewPassword;
                });
              },
            ),
            const SizedBox(height: 16),

            _buildPasswordField(
              controller: _confirmPasswordController,
              label: 'Yeni şifre tekrar',
              obscureText: _obscureConfirmPassword,
              onVisibilityChanged: () {
                setState(() {
                  _obscureConfirmPassword =
                  !_obscureConfirmPassword;
                });
              },
            ),
            const SizedBox(height: 12),

            const Text(
              'Şifren en az 8, en fazla 128 karakter olmalıdır.',
              style: TextStyle(
                color: Colors.white54,
                fontSize: 12,
              ),
            ),
            const SizedBox(height: 26),

            SizedBox(
              height: 52,
              child: ElevatedButton(
                onPressed: _isLoading ? null : _submit,
                child: _isLoading
                    ? const SizedBox(
                  width: 22,
                  height: 22,
                  child: CircularProgressIndicator(
                    strokeWidth: 2,
                  ),
                )
                    : Text(_pageTitle),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
