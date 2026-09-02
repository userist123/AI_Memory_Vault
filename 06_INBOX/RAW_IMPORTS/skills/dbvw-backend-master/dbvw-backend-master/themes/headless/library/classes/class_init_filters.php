<?php

class WS_Init_Filters extends WS_Filter_Set {

	/**
	 * Constructor
	 *
	 */
	public function __construct() {
		parent::__construct( array(
			'upload_mimes' 			=> 'svg_mime_types',
			'tiny_mce_before_init'	=> 'my_format_TinyMCE',
			'rest_query_vars'       => 'my_allow_meta_query',
			'wp_get_attachment_url' => 'rewrite_cdn_url',
			 'jpeg_quality'         => 'set_jpeg_quality'
			));
	}

	public function svg_mime_types( $mimes ) {
		$mimes['svg'] = 'image/svg+xml';
		return $mimes;
	}


	public function my_format_TinyMCE( $in ) {
		$in['remove_linebreaks'] = false;
		$in['gecko_spellcheck'] = false;
		$in['keep_styles'] = true;
		$in['accessibility_focus'] = true;
		$in['tabfocus_elements'] = 'major-publishing-actions';
		$in['media_strict'] = false;
		$in['paste_remove_styles'] = false;
		$in['paste_remove_spans'] = false;
		$in['paste_strip_class_attributes'] = 'none';
		$in['paste_text_use_dialog'] = true;
		$in['wpeditimage_disable_captions'] = true;
		//$in['plugins'] = 'tabfocus,paste,media,fullscreen,wordpress,wpeditimage,wpgallery,wplink,wpdialogs,wpfullscreen';
		//$in['content_css'] = get_template_directory_uri() . "/editor-style.css";
		$in['wpautop'] = true;
		$in['apply_source_formatting'] = false;
		$in['block_formats'] = "Paragraph=p; Heading 3=h3; Heading 4=h4; Heading 5=h5";
		$in['toolbar1'] = 'formatselect,bold,italic,underline,bullist,numlist,link,unlink';
		$in['toolbar2'] = '';
		$in['toolbar3'] = '';
		$in['toolbar4'] = '';
		return $in;
	}

	public function my_allow_meta_query( $valid_vars ) {
		$valid_vars = array_merge( $valid_vars, array( 'meta_key', 'meta_value', 'meta_compare' ) );
		return $valid_vars;
	}

	/**
	 * Rewrite attachment URL from the base CMS form to the desired CDN form.
	 *
	 * @filter 'wp_get_attachment_url'
	 * @param $original string the original attachment URL
	 * @return the updated CDN url.
	 */
	public function rewrite_cdn_url( $original ) {
		$trailing_string = '/wp-content/uploads/';
		$cms_url =  get_option( 'siteurl' );
		$cdn_url = get_option('cdn_url');
		if ( ! empty( $cdn_url ) ) {
			return str_replace( $cms_url . $trailing_string, $cdn_url . '/', $original );
		} else {
			return $original;
		}
	}

	public function set_jpeg_quality( $arg ) { return 90; }


}

new WS_Init_Filters();
?>