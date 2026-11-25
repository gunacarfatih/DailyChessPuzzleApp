import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../providers/auth_provider.dart';
import 'email_verification_page.dart';

class RegisterPage extends StatefulWidget {
  const RegisterPage({super.key});

  @override
  State<RegisterPage> createState() => _RegisterPageState();
}

class _RegisterPageState extends State<RegisterPage> {
  final GlobalKey<FormState> _formKey = GlobalKey<FormState>();

  final TextEditingController _usernameController = TextEditingController();
  final TextEditingController _firstNameController = TextEditingController();
  final TextEditingController _lastNameController = TextEditingController();
  final TextEditingController _emailController = TextEditingController();
  final TextEditingController _passwordController = TextEditingController();
  final TextEditingController _confirmPasswordController =
  TextEditingController();

  bool _obscurePassword = true;
  bool _obscureConfirmPassword = true;
  String? _pendingVerificationEmail;

  @override
  void dispose() {
    _usernameController.dispose();
    _firstNameController.dispose();
    _lastNameController.dispose();
    _emailController.dispose();
    _passwordController.dispose();
    _confirmPasswordController.dispose();
    super.dispose();
  }

  Future<void> _register() async {
    if (_pendingVerificationEmail != null) {
      await _verifyEmail();
      return;
    }

    if (!_formKey.currentState!.validate()) {
      return;
    }

    try {
      final response = await context.read<AuthProvider>().register(
        username: _usernameController.text.trim(),
        firstName: _firstNameController.text.trim(),
        lastName: _lastNameController.text.trim(),
        email: _emailController.text.trim(),
        password: _passwordController.text,
      );

      if (!mounted) return;

      if (response.success && response.data != null) {
        setState(() {
          _pendingVerificationEmail =
              response.data!['email']?.toString() ??
                  _emailController.text.trim();
        });
        await _verifyEmail();
      } else if (response.errorCode == 'EMAIL_DELIVERY_FAILED') {
        setState(() {
          _pendingVerificationEmail = _emailController.text.trim();
        });
        _showError(
          'Hesap oluşturuldu, ancak e-posta gönderilemedi. Kodu yeniden iste.',
        );
        await _verifyEmail();
      } else {
        _showError(response.message ?? 'Kayıt işlemi başarısız.');
      }
    } catch (e) {
      if (!mounted) return;

      _showError(
        'Sunucuya bağlanırken bir hata oluştu. '
            'Lütfen tekrar deneyin.',
      );
    }
  }

  Future<void> _verifyEmail() async {
    final email = _pendingVerificationEmail;
    if (email == null) return;

    final verified = await Navigator.of(context).push<bool>(
      MaterialPageRoute(builder: (_) => EmailVerificationPage(email: email)),
    );
    if (!mounted || verified != true) return;
    Navigator.pop(context, true);
  }

  Future<void> _resendVerification() async {
    final email = _pendingVerificationEmail;
    if (email == null) return;

    try {
      final result = await context.read<AuthProvider>().resendVerification(
        email: email,
      );
      if (!mounted) return;
      if (result.success) {
        _showInfo('Kod gönderme isteği alındı. Gelen kutunu kontrol et.');
      } else {
        _showError(result.message ?? 'Kod yeniden gönderilemedi.');
      }
    } catch (_) {
      if (mounted) _showError('Kod yeniden gönderilemedi.');
    }
  }

  void _showError(String message) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(message), backgroundColor: Colors.red),
    );
  }

  void _showInfo(String message) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(message), backgroundColor: Colors.green),
    );
  }

  String? _validateName(String? value, String fieldName) {
    final name = value?.trim() ?? '';

    if (name.isEmpty) {
      return '$fieldName gir.';
    }

    if (name.length > 50) {
      return '$fieldName en fazla 50 karakter olabilir.';
    }

    return null;
  }

  @override
  Widget build(BuildContext context) {
    final isLoading = context.watch<AuthProvider>().isLoading;

    return Scaffold(
      appBar: AppBar(title: const Text('Hesap Oluştur')),
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: Form(
              key: _formKey,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Icon(
                    Icons.person_add_alt_1,
                    size: 72,
                    color: Colors.amber,
                  ),

                  const SizedBox(height: 20),

                  const Text(
                    'Aramıza Katıl!',
                    textAlign: TextAlign.center,
                    style: TextStyle(fontSize: 26, fontWeight: FontWeight.bold),
                  ),

                  const SizedBox(height: 8),

                  Text(
                    'Hesabını oluştur ve satranç yolculuğuna '
                        'kendi hesabınla devam et.',
                    textAlign: TextAlign.center,
                    style: TextStyle(color: Colors.grey.shade400),
                  ),

                  const SizedBox(height: 30),

                  // Kullanıcı adı
                  TextFormField(
                    controller: _usernameController,
                    textCapitalization: TextCapitalization.none,
                    textInputAction: TextInputAction.next,
                    decoration: const InputDecoration(
                      labelText: 'Kullanıcı adı',
                      prefixIcon: Icon(Icons.person_outline),
                      border: OutlineInputBorder(),
                    ),
                    validator: (value) {
                      final username = value?.trim() ?? '';

                      if (username.isEmpty) {
                        return 'Kullanıcı adı gir.';
                      }

                      if (username.length < 3 || username.length > 50) {
                        return 'Kullanıcı adı 3-50 karakter olmalı.';
                      }

                      return null;
                    },
                  ),

                  const SizedBox(height: 16),

                  // Ad
                  TextFormField(
                    controller: _firstNameController,
                    textCapitalization: TextCapitalization.words,
                    textInputAction: TextInputAction.next,
                    decoration: const InputDecoration(
                      labelText: 'Ad',
                      prefixIcon: Icon(Icons.badge_outlined),
                      border: OutlineInputBorder(),
                    ),
                    validator: (value) => _validateName(value, 'Ad'),
                  ),

                  const SizedBox(height: 16),

                  // Soyad
                  TextFormField(
                    controller: _lastNameController,
                    textCapitalization: TextCapitalization.words,
                    textInputAction: TextInputAction.next,
                    decoration: const InputDecoration(
                      labelText: 'Soyad',
                      prefixIcon: Icon(Icons.badge_outlined),
                      border: OutlineInputBorder(),
                    ),
                    validator: (value) => _validateName(value, 'Soyad'),
                  ),

                  const SizedBox(height: 16),

                  // E-posta
                  TextFormField(
                    controller: _emailController,
                    keyboardType: TextInputType.emailAddress,
                    textInputAction: TextInputAction.next,
                    decoration: const InputDecoration(
                      labelText: 'E-posta',
                      prefixIcon: Icon(Icons.email_outlined),
                      border: OutlineInputBorder(),
                    ),
                    validator: (value) {
                      final email = value?.trim() ?? '';

                      if (email.isEmpty) {
                        return 'E-posta adresi gir.';
                      }

                      if (!RegExp(r'^[^@\s]+@[^@\s]+\.[^@\s]+$')
                          .hasMatch(email)) {
                        return 'Geçerli bir e-posta gir.';
                      }

                      return null;
                    },
                  ),

                  const SizedBox(height: 16),

                  // Şifre
                  TextFormField(
                    controller: _passwordController,
                    obscureText: _obscurePassword,
                    textInputAction: TextInputAction.next,
                    decoration: InputDecoration(
                      labelText: 'Şifre',
                      prefixIcon: const Icon(Icons.lock_outline),
                      border: const OutlineInputBorder(),
                      suffixIcon: IconButton(
                        icon: Icon(
                          _obscurePassword
                              ? Icons.visibility_outlined
                              : Icons.visibility_off_outlined,
                        ),
                        onPressed: () {
                          setState(() {
                            _obscurePassword = !_obscurePassword;
                          });
                        },
                      ),
                    ),
                    validator: (value) {
                      if (value == null || value.isEmpty) {
                        return 'Şifre gir.';
                      }

                      if (value.length < 8 || value.length > 128) {
                        return 'Şifre 8-128 karakter olmalı.';
                      }

                      return null;
                    },
                  ),

                  const SizedBox(height: 16),

                  // Şifre tekrar
                  TextFormField(
                    controller: _confirmPasswordController,
                    obscureText: _obscureConfirmPassword,
                    textInputAction: TextInputAction.done,
                    onFieldSubmitted: (_) {
                      if (!isLoading) _register();
                    },
                    decoration: InputDecoration(
                      labelText: 'Şifre tekrar',
                      prefixIcon: const Icon(Icons.lock_outline),
                      border: const OutlineInputBorder(),
                      suffixIcon: IconButton(
                        icon: Icon(
                          _obscureConfirmPassword
                              ? Icons.visibility_outlined
                              : Icons.visibility_off_outlined,
                        ),
                        onPressed: () {
                          setState(() {
                            _obscureConfirmPassword = !_obscureConfirmPassword;
                          });
                        },
                      ),
                    ),
                    validator: (value) {
                      if (value == null || value.isEmpty) {
                        return 'Şifreni tekrar gir.';
                      }

                      if (value != _passwordController.text) {
                        return 'Şifreler eşleşmiyor.';
                      }

                      return null;
                    },
                  ),

                  const SizedBox(height: 26),

                  SizedBox(
                    height: 52,
                    child: ElevatedButton(
                      onPressed: isLoading ? null : _register,
                      child: isLoading
                          ? const SizedBox(
                        width: 22,
                        height: 22,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                          : Text(
                        _pendingVerificationEmail == null
                            ? 'Hesap Oluştur'
                            : 'E-postayı Doğrula',
                        style: const TextStyle(fontSize: 16),
                      ),
                    ),
                  ),

                  if (_pendingVerificationEmail != null) ...[
                    const SizedBox(height: 8),
                    TextButton(
                      onPressed: isLoading ? null : _resendVerification,
                      child: const Text('Doğrulama kodunu yeniden gönder'),
                    ),
                  ],

                  const SizedBox(height: 16),

                  Text(
                    'Yeni hesabın sıfırdan başlayacak. '
                        'Misafir oyun geçmişin ve puanların ayrı kalacak.',
                    textAlign: TextAlign.center,
                    style: TextStyle(color: Colors.grey.shade400, fontSize: 12),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
